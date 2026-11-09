using System.IO;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.TikTok;

/// <summary>Como está a leitura do chat do TikTok (o ponto do TikTok na faixa do canal).</summary>
public enum EstadoDoTikTok
{
    /// <summary>Sem Chat Multiplataforma ou sem canal do TikTok.</summary>
    Desligado,

    /// <summary>Procurando a live do canal, ou conectando ao chat dela.</summary>
    Procurando,

    /// <summary>Lendo o chat da live.</summary>
    Conectado,

    /// <summary>O canal não está ao vivo (ou a live acabou): tenta de novo de tempos em tempos.</summary>
    EsperandoALive,

    /// <summary>O TikTok disse que o perfil não existe: não tenta de novo até trocar o canal.</summary>
    CanalNaoExiste,

    /// <summary>Sem internet ou o TikTok não respondeu: tenta de novo daqui a pouco.</summary>
    SemConexao,
}

/// <summary>
/// Lê o chat de uma live do TikTok, só leitura, pelo mesmo WebSocket que o site usa para quem assiste sem conta (ver
/// <see cref="ConexaoComOTikTok"/>): procura a sala da live do canal, conecta, entra como espectador e recebe as
/// mensagens. Quando o canal não está ao vivo, ou a live acaba, espera e procura de novo; quando o perfil não existe,
/// para. Os avisos chegam em outra thread (quem usa passa para a da tela).
/// </summary>
public sealed class LeitorDoTikTok : IDisposable
{
    /// <summary>Entre uma procura e outra quando o canal não está ao vivo.</summary>
    public static readonly TimeSpan EsperaPorLive = TimeSpan.FromSeconds(60);

    /// <summary>Entre uma tentativa e outra quando o TikTok não respondeu.</summary>
    public static readonly TimeSpan EsperaSemConexao = TimeSpan.FromSeconds(20);

    /// <summary>A conexão caiu no meio da live: conecta de novo logo (as mensagens desse intervalo se perdem).</summary>
    public static readonly TimeSpan EsperaParaReconectar = TimeSpan.FromSeconds(2);

    /// <summary>Sem nada do TikTok por este tempo (nem a resposta das batidas), a conexão é dada como caída.</summary>
    public static readonly TimeSpan TempoSemNada = TimeSpan.FromSeconds(40);

    // O TikTok pode mandar de novo as mesmas mensagens (o histórico, depois de reconectar); as últimas ficam guardadas
    private const int IdsGuardados = 500;

    private readonly IConexaoComOTikTok _tikTok;
    private readonly ILogger _log;
    private readonly TimeSpan _esperaPorLive;
    private readonly TimeSpan _esperaSemConexao;
    private readonly TimeSpan _batidas;
    private readonly TimeSpan _tempoSemNada;
    private readonly TimeSpan _relogio;
    private readonly object _trava = new();
    // A leitura atual; trocar de canal ou parar a cancela, e os avisos de uma leitura antiga são ignorados
    private CancellationTokenSource? _leitura;
    // Os nomes dos presentes em português, da última sala (a lista tem uns 3 MB: uma vez por sala)
    private IReadOnlyDictionary<long, string> _nomesDosPresentes = new Dictionary<long, string>();
    private string? _salaDosNomes;
    private readonly JuntadorDePresentes _presentes;

    /// <param name="conta">A conta do TikTok conectada (aba TikTok): com ela, lê como quem está logado.</param>
    public LeitorDoTikTok(ILogger<LeitorDoTikTok> log, ContaDoTikTok? conta = null)
        : this(log, new ConexaoComOTikTok(conta == null ? null : () => conta.Sessao), EsperaPorLive, EsperaSemConexao, ConexaoComOTikTok.IntervaloDasBatidas, TempoSemNada,
            TimeSpan.FromSeconds(1))
    {
    }

    /// <param name="tikTok">Os pedidos ao TikTok (os testes usam um falso).</param>
    /// <param name="relogio">De quanto em quanto tempo os presentes que esperam terminar são conferidos.</param>
    public LeitorDoTikTok(ILogger log, IConexaoComOTikTok tikTok, TimeSpan esperaPorLive, TimeSpan esperaSemConexao, TimeSpan batidas,
        TimeSpan tempoSemNada, TimeSpan relogio)
    {
        _log = log;
        _tikTok = tikTok;
        _esperaPorLive = esperaPorLive;
        _esperaSemConexao = esperaSemConexao;
        _batidas = batidas;
        _tempoSemNada = tempoSemNada;
        _relogio = relogio;
        _presentes = new JuntadorDePresentes(id => _nomesDosPresentes.GetValueOrDefault(id));
    }

    /// <summary>O estado mudou (em outra thread).</summary>
    public event Action<EstadoDoTikTok>? EstadoMudou;

    /// <summary>Uma mensagem do chat (em outra thread).</summary>
    public event Action<ItemDoTikTok>? MensagemRecebida;

    /// <summary>Um moderador apagou mensagens: pelos ids delas e pelo @ de quem mandou (em outra thread).</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>>? MensagensApagadas;

    /// <summary>Um presente (o combo inteiro), alguém que seguiu, compartilhou ou virou Superfã (em outra thread).</summary>
    public event Action<AvisoDoTikTok>? AvisoRecebido;

    /// <summary>
    /// Quantos assistem a live agora (em outra thread): ao conectar, se a sala disse, e depois quando o TikTok conta de
    /// novo (mais ou menos a cada 10 a 30 s). Ao sair de Conectado o número vale nada (ver <see cref="EstadoMudou"/>).
    /// </summary>
    public event Action<long>? ViewersMudou;

    /// <summary>O TikTok não aceita mais a sessão da conta conectada (em outra thread): a leitura segue sem ela.</summary>
    public event Action? SessaoRecusada;

    /// <summary>O nome do canal (sem o @), ou null.</summary>
    public string? Canal { get; private set; }

    public EstadoDoTikTok Estado { get; private set; }

    /// <summary>Mostrar as mensagens que o TikTok manda ao entrar (opção "Mostrar as mensagens de antes de conectar").</summary>
    public bool MostrarHistorico { get; set; }

    /// <summary>
    /// Começa a ler o chat do canal. O mesmo canal já ligado não recomeça (as mensagens continuam); um canal que não
    /// existia é procurado de novo.
    /// </summary>
    public void Ligar(string canal)
    {
        var leitura = new CancellationTokenSource();
        lock (_trava)
        {
            if (canal == Canal && Estado is not (EstadoDoTikTok.Desligado or EstadoDoTikTok.CanalNaoExiste))
                return;
            Parar();
            Canal = canal;
            _leitura = leitura;
            _log.LogInformation("TikTok: lendo o chat de @{Canal}.", canal);
        }
        Mudar(EstadoDoTikTok.Procurando, leitura);
        _ = Task.Run(() => LerAsync(canal, leitura));
    }

    /// <summary>Para de ler (Chat Multiplataforma desligado, canal do TikTok apagado ou o app fechando).</summary>
    public void Desligar()
    {
        lock (_trava)
        {
            if (Canal == null && Estado == EstadoDoTikTok.Desligado)
                return;
            Parar();
            Canal = null;
        }
        Mudar(EstadoDoTikTok.Desligado);
    }

    /// <summary>
    /// Conecta de novo ao mesmo canal (a conta do TikTok conectou ou desconectou): as mensagens na tela continuam, e
    /// as que chegarem nesse instante se perdem.
    /// </summary>
    public void Recomecar()
    {
        string? canal;
        lock (_trava)
        {
            canal = Canal;
            if (canal == null || Estado is EstadoDoTikTok.Desligado or EstadoDoTikTok.CanalNaoExiste)
                return;
            Parar();
            Canal = null;
        }
        Ligar(canal);
    }

    public void Dispose() => Desligar();

    // Com a trava. Só pede para parar; não espera
    private void Parar()
    {
        _leitura?.Cancel();
        _leitura = null;
    }

    // Procura a live, lê o chat até ela acabar e espera para procurar de novo; até cancelar ou o canal não existir
    private async Task LerAsync(string canal, CancellationTokenSource leitura)
    {
        CancellationToken cancelar = leitura.Token;
        var vistas = new Vistas();
        try
        {
            while (true)
            {
                (EstadoDoTikTok parou, bool conectou) = await LerUmaVezAsync(canal, leitura, vistas).ConfigureAwait(false);
                if (parou == EstadoDoTikTok.SemConexao && conectou)
                {
                    // Caiu no meio da live: o ponto fica amarelo enquanto conecta de novo
                    Mudar(EstadoDoTikTok.Procurando, leitura);
                    await Task.Delay(EsperaParaReconectar < _esperaSemConexao ? EsperaParaReconectar : _esperaSemConexao, cancelar).ConfigureAwait(false);
                    continue;
                }
                Mudar(parou, leitura);
                if (parou == EstadoDoTikTok.CanalNaoExiste)
                    return;
                await Task.Delay(parou == EstadoDoTikTok.SemConexao ? _esperaSemConexao : _esperaPorLive, cancelar).ConfigureAwait(false);
                Mudar(EstadoDoTikTok.Procurando, leitura);
            }
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
        }
    }

    // Uma procura; devolve por que parou (canal fora do ar, live acabou, perfil que não existe ou falha de conexão) e se
    // chegou a ler o chat
    private async Task<(EstadoDoTikTok Parou, bool Conectou)> LerUmaVezAsync(string canal, CancellationTokenSource leitura, Vistas vistas)
    {
        CancellationToken cancelar = leitura.Token;
        SalaDoTikTok sala;
        ISocketDoTikTok socket;
        try
        {
            sala = SalaDoTikTok.Ler(await _tikTok.SalaAsync(canal, cancelar).ConfigureAwait(false));
            if (!sala.Existe)
            {
                _log.LogInformation("TikTok: o perfil @{Canal} não existe.", canal);
                return (EstadoDoTikTok.CanalNaoExiste, false);
            }
            if (!sala.AoVivo)
            {
                _log.LogInformation("TikTok: @{Canal} não está ao vivo.", canal);
                return (EstadoDoTikTok.EsperandoALive, false);
            }
            // Com a conta conectada, confere antes se o TikTok ainda aceita a sessão (uma recusada viraria leitura sem
            // conta sem ninguém saber): recusada, avisa e esta conexão já vai sem ela
            if (_tikTok.ComSessao && await _tikTok.ConferirSessaoAsync(cancelar).ConfigureAwait(false) == SituacaoDaSessao.Recusada)
            {
                _log.LogInformation("TikTok: a sessão da conta conectada foi recusada; lendo sem conta.");
                SessaoRecusada?.Invoke();
            }
            socket = await _tikTok.AbrirAsync(sala.IdDaSala, cancelar).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancelar.IsCancellationRequested)
        {
            _log.LogInformation("TikTok: não deu para abrir o chat de @{Canal}: {Erro}", canal, e.Message);
            return (EstadoDoTikTok.SemConexao, false);
        }

        using (socket)
        using (var fim = CancellationTokenSource.CreateLinkedTokenSource(cancelar))
        {
            var envio = new SemaphoreSlim(1, 1);
            try
            {
                await Enviar(socket, envio, QuadrosDoTikTok.EntrarNaSala(sala.IdDaSala), cancelar).ConfigureAwait(false);
                _log.LogInformation("TikTok: lendo o chat da live {Sala}.", sala.IdDaSala);
                Mudar(EstadoDoTikTok.Conectado, leitura);
                if (sala.Viewers is { } viewers)
                    ViewersMudou?.Invoke(viewers);
                _ = BaterAsync(socket, envio, sala.IdDaSala, fim.Token);
                _presentes.Limpar();
                _ = RelogioDosPresentesAsync(leitura, fim.Token);
                _ = BuscarNomesDosPresentesAsync(sala.IdDaSala, fim.Token);

                while (true)
                {
                    using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
                    prazo.CancelAfter(_tempoSemNada);
                    byte[]? quadro = await socket.ReceberAsync(prazo.Token).ConfigureAwait(false);
                    if (quadro == null)
                    {
                        _log.LogInformation("TikTok: o chat da live {Sala} fechou a conexão.", sala.IdDaSala);
                        return (EstadoDoTikTok.SemConexao, true);
                    }

                    LoteDoTikTok lote;
                    try
                    {
                        lote = QuadrosDoTikTok.Ler(quadro, sala.IdDoDono, MostrarHistorico);
                    }
                    catch (InvalidDataException e)
                    {
                        _log.LogInformation("TikTok: um quadro do chat não foi entendido: {Erro}", e.Message);
                        continue;
                    }
                    if (lote.Confirmar != 0)
                        await Enviar(socket, envio, QuadrosDoTikTok.Confirmar(lote.Confirmar), cancelar).ConfigureAwait(false);
                    Receber(lote, leitura, vistas);
                    if (lote.LiveAcabou)
                    {
                        _log.LogInformation("TikTok: a live {Sala} acabou.", sala.IdDaSala);
                        return (EstadoDoTikTok.EsperandoALive, true);
                    }
                }
            }
            catch (Exception e) when (!cancelar.IsCancellationRequested)
            {
                _log.LogInformation("TikTok: a conexão com o chat da live {Sala} caiu: {Erro}", sala.IdDaSala, e.Message);
                return (EstadoDoTikTok.SemConexao, true);
            }
            finally
            {
                fim.Cancel(); // para as batidas
            }
        }
    }

    // O "estou aqui" a cada tantos segundos, até a leitura acabar (um erro aqui aparece na leitura, que cai junto)
    private async Task BaterAsync(ISocketDoTikTok socket, SemaphoreSlim envio, string idDaSala, CancellationToken fim)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_batidas, fim).ConfigureAwait(false);
                await Enviar(socket, envio, QuadrosDoTikTok.Batida(idDaSala), fim).ConfigureAwait(false);
            }
        }
        catch (Exception) when (fim.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            _log.LogInformation("TikTok: a batida não foi: {Erro}", e.Message);
        }
    }

    // Os presentes que esperam terminar (o combo sem final, os repetidos sem combo) saem quando passa o tempo
    private async Task RelogioDosPresentesAsync(CancellationTokenSource leitura, CancellationToken fim)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_relogio, fim).ConfigureAwait(false);
                foreach (AvisoDoTikTok aviso in _presentes.Vencidos(DateTimeOffset.UtcNow))
                    Avisar(aviso, leitura);
            }
        }
        catch (OperationCanceledException) when (fim.IsCancellationRequested)
        {
        }
    }

    // Os nomes dos presentes em português; sem eles (o TikTok não respondeu), ficam os nomes em inglês das mensagens
    private async Task BuscarNomesDosPresentesAsync(string idDaSala, CancellationToken fim)
    {
        if (_salaDosNomes == idDaSala)
            return;
        try
        {
            IReadOnlyDictionary<long, string> nomes = ListaDePresentes.Ler(await _tikTok.PresentesAsync(idDaSala, fim).ConfigureAwait(false));
            if (nomes.Count > 0)
            {
                _nomesDosPresentes = nomes;
                _salaDosNomes = idDaSala;
            }
        }
        catch (Exception e) when (!fim.IsCancellationRequested)
        {
            _log.LogInformation("TikTok: a lista de presentes não veio: {Erro}", e.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Avisar(AvisoDoTikTok aviso, CancellationTokenSource leitura)
    {
        lock (_trava)
        {
            if (leitura != _leitura)
                return;
        }
        AvisoRecebido?.Invoke(aviso);
    }

    // O WebSocket aceita um envio de cada vez (as batidas e as confirmações se cruzam)
    private static async Task Enviar(ISocketDoTikTok socket, SemaphoreSlim envio, byte[] quadro, CancellationToken cancelar)
    {
        await envio.WaitAsync(cancelar).ConfigureAwait(false);
        try
        {
            await socket.EnviarAsync(quadro, cancelar).ConfigureAwait(false);
        }
        finally
        {
            envio.Release();
        }
    }

    private void Receber(LoteDoTikTok lote, CancellationTokenSource leitura, Vistas vistas)
    {
        lock (_trava)
        {
            if (leitura != _leitura)
                return;
        }
        foreach (ItemDoTikTok item in lote.Mensagens)
        {
            if (vistas.Nova(item))
                MensagemRecebida?.Invoke(item);
        }
        if (lote.Apagadas.Count > 0 || lote.UsuariosApagados.Count > 0)
            MensagensApagadas?.Invoke(lote.Apagadas, vistas.LoginsDe(lote.UsuariosApagados));
        DateTimeOffset agora = DateTimeOffset.UtcNow;
        foreach (EnvioDePresente presente in lote.Presentes)
        {
            if (_presentes.Receber(presente, agora) is { } aviso)
                AvisoRecebido?.Invoke(aviso);
        }
        foreach (AvisoDoTikTok aviso in lote.Avisos)
            AvisoRecebido?.Invoke(aviso);
        if (lote.Viewers is { } viewers)
            ViewersMudou?.Invoke(viewers);
    }

    // Sem leitura vale sempre (Desligar); com leitura, só se ela ainda for a atual
    private void Mudar(EstadoDoTikTok estado, CancellationTokenSource? leitura = null)
    {
        lock (_trava)
        {
            if ((leitura != null && leitura != _leitura) || Estado == estado)
                return;
            Estado = estado;
        }
        EstadoMudou?.Invoke(estado);
    }

    // As últimas mensagens que apareceram (para não repetir) e o @ de quem mandou (o TikTok apaga as de uma pessoa
    // pelo id dela, e a página só conhece o @)
    private sealed class Vistas
    {
        private readonly HashSet<string> _ids = [];
        private readonly Queue<string> _ordem = new();
        private readonly Dictionary<string, string> _logins = [];

        public bool Nova(ItemDoTikTok item)
        {
            if (item.Login.Length > 0)
            {
                if (_logins.Count >= IdsGuardados * 2)
                    _logins.Clear();
                _logins[item.IdDoUsuario] = item.Login;
            }
            if (item.Id is "" or "0")
                return true;
            if (!_ids.Add(item.Id))
                return false;
            _ordem.Enqueue(item.Id);
            if (_ordem.Count > IdsGuardados)
                _ids.Remove(_ordem.Dequeue());
            return true;
        }

        public IReadOnlyList<string> LoginsDe(IReadOnlyList<string> usuarios) =>
            [.. usuarios.Select(u => _logins.GetValueOrDefault(u)).OfType<string>().Distinct()];
    }
}
