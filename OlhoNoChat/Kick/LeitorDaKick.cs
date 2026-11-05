using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Kick;

/// <summary>Como está a leitura do chat da Kick (o ponto da Kick na faixa do canal).</summary>
public enum EstadoDaKick
{
    /// <summary>Sem Chat Multiplataforma ou sem canal da Kick.</summary>
    Desligado,

    /// <summary>Procurando o canal ou conectando ao chat dele (também ao conectar de novo depois de uma queda).</summary>
    Conectando,

    /// <summary>Lendo o chat do canal (o chat da Kick funciona com o canal fora do ar também).</summary>
    Conectado,

    /// <summary>Sem internet ou a Kick não respondeu: tenta de novo daqui a pouco.</summary>
    SemConexao,

    /// <summary>A Kick disse que o canal não existe: não tenta de novo até trocar o canal.</summary>
    CanalNaoExiste,
}

/// <summary>
/// Lê o chat de um canal da Kick, só leitura, pelo mesmo tempo real que o site usa para quem assiste sem conta (o
/// Centrifugo; ver <see cref="ConexaoComAKick"/>): procura o canal, pede um token de convidado e o endereço, conecta,
/// assina a sala do chat e recebe os eventos. Responde aos pings, troca o token antes de vencer e, quando a conexão cai,
/// conecta de novo. Os avisos chegam em outra thread (quem usa passa para a da tela).
/// </summary>
public sealed class LeitorDaKick : IDisposable
{
    /// <summary>Entre uma tentativa e outra quando a Kick não respondeu.</summary>
    public static readonly TimeSpan EsperaSemConexao = TimeSpan.FromSeconds(20);

    /// <summary>A conexão caiu no meio da leitura: conecta de novo logo (as mensagens desse intervalo se perdem).</summary>
    public static readonly TimeSpan EsperaParaReconectar = TimeSpan.FromSeconds(2);

    /// <summary>O servidor manda um ping a cada 25 s: sem nada por este tempo, a conexão é dada como caída.</summary>
    public static readonly TimeSpan TempoSemNada = TimeSpan.FromSeconds(35);

    // O token vale uns 45 minutos: troca este tempo antes de vencer
    private static readonly TimeSpan MargemDoToken = TimeSpan.FromMinutes(5);

    private readonly IConexaoComAKick _kick;
    private readonly ILogger _log;
    private readonly TimeSpan _esperaSemConexao;
    private readonly TimeSpan _tempoSemNada;
    private readonly TimeSpan? _renovarACada;
    private readonly object _trava = new();
    // A leitura atual; trocar de canal ou parar a cancela, e os avisos de uma leitura antiga são ignorados
    private CancellationTokenSource? _leitura;

    public LeitorDaKick(ILogger<LeitorDaKick> log)
        : this(log, new ConexaoComAKick(), EsperaSemConexao, TempoSemNada, renovarACada: null)
    {
    }

    /// <param name="kick">Os pedidos à Kick (os testes usam uma falsa).</param>
    /// <param name="renovarACada">De quanto em quanto tempo trocar o token; null = pelo tempo de vida que a Kick der.</param>
    public LeitorDaKick(ILogger log, IConexaoComAKick kick, TimeSpan esperaSemConexao, TimeSpan tempoSemNada, TimeSpan? renovarACada)
    {
        _log = log;
        _kick = kick;
        _esperaSemConexao = esperaSemConexao;
        _tempoSemNada = tempoSemNada;
        _renovarACada = renovarACada;
    }

    /// <summary>O estado mudou (em outra thread).</summary>
    public event Action<EstadoDaKick>? EstadoMudou;

    /// <summary>Uma mensagem, uma mensagem apagada, um banimento ou o chat limpo (em outra thread).</summary>
    public event Action<EventoDaKick>? EventoRecebido;

    /// <summary>O nome do canal (como em <see cref="CanalDaKick.Ler"/>), ou null.</summary>
    public string? Canal { get; private set; }

    public EstadoDaKick Estado { get; private set; }

    /// <summary>
    /// Começa a ler o chat do canal. O mesmo canal já ligado não recomeça (as mensagens continuam); um canal que não
    /// existia é procurado de novo.
    /// </summary>
    public void Ligar(string canal)
    {
        var leitura = new CancellationTokenSource();
        lock (_trava)
        {
            if (canal == Canal && Estado is not (EstadoDaKick.Desligado or EstadoDaKick.CanalNaoExiste))
                return;
            Parar();
            Canal = canal;
            _leitura = leitura;
            _log.LogInformation("Kick: lendo o chat de {Canal}.", canal);
        }
        Mudar(EstadoDaKick.Conectando, leitura);
        _ = Task.Run(() => LerAsync(canal, leitura));
    }

    /// <summary>Para de ler (Chat Multiplataforma desligado, canal da Kick apagado ou o app fechando).</summary>
    public void Desligar()
    {
        lock (_trava)
        {
            if (Canal == null && Estado == EstadoDaKick.Desligado)
                return;
            Parar();
            Canal = null;
        }
        Mudar(EstadoDaKick.Desligado);
    }

    public void Dispose() => Desligar();

    // Com a trava. Só pede para parar; não espera
    private void Parar()
    {
        _leitura?.Cancel();
        _leitura = null;
    }

    // Lê até cancelar ou o canal não existir; quando cai, conecta de novo
    private async Task LerAsync(string canal, CancellationTokenSource leitura)
    {
        CancellationToken cancelar = leitura.Token;
        try
        {
            while (true)
            {
                (EstadoDaKick parou, bool conectou) = await LerUmaVezAsync(canal, leitura).ConfigureAwait(false);
                if (parou == EstadoDaKick.SemConexao && conectou)
                {
                    // Caiu no meio da leitura: o ponto fica amarelo enquanto conecta de novo
                    Mudar(EstadoDaKick.Conectando, leitura);
                    await Task.Delay(EsperaParaReconectar < _esperaSemConexao ? EsperaParaReconectar : _esperaSemConexao, cancelar).ConfigureAwait(false);
                    continue;
                }
                Mudar(parou, leitura);
                if (parou == EstadoDaKick.CanalNaoExiste)
                    return;
                await Task.Delay(_esperaSemConexao, cancelar).ConfigureAwait(false);
                Mudar(EstadoDaKick.Conectando, leitura);
            }
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
        }
    }

    // Uma conexão; devolve por que parou (canal que não existe ou falha de conexão) e se chegou a ler o chat
    private async Task<(EstadoDaKick Parou, bool Conectou)> LerUmaVezAsync(string canal, CancellationTokenSource leitura)
    {
        CancellationToken cancelar = leitura.Token;
        SalaDaKick sala;
        string cliente = Guid.NewGuid().ToString();
        string token;
        ISocketDaKick socket;
        try
        {
            string? json = await _kick.CanalAsync(canal, cancelar).ConfigureAwait(false);
            // "fulano_1" quase sempre tem o endereço kick.com/fulano-1, mas há canais com o "_" no endereço
            if (json == null && CanalDaKick.OutraForma(canal) is { } outraForma)
                json = await _kick.CanalAsync(outraForma, cancelar).ConfigureAwait(false);
            if (json == null)
            {
                _log.LogInformation("Kick: o canal {Canal} não existe.", canal);
                return (EstadoDaKick.CanalNaoExiste, false);
            }
            sala = SalaDaKick.Ler(json);
            token = await _kick.TokenAsync(cliente, cancelar).ConfigureAwait(false);
            string endereco = await _kick.EnderecoAsync(sala.IdDoCanal, cliente, cancelar).ConfigureAwait(false);
            socket = await _kick.AbrirAsync(endereco, cancelar).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancelar.IsCancellationRequested)
        {
            _log.LogInformation("Kick: não deu para abrir o chat de {Canal}: {Erro}", canal, e.Message);
            return (EstadoDaKick.SemConexao, false);
        }

        using (socket)
        using (var fim = CancellationTokenSource.CreateLinkedTokenSource(cancelar))
        {
            var envio = new SemaphoreSlim(1, 1);
            bool conectou = false;
            try
            {
                await Enviar(socket, envio, QuadrosDaKick.ConectarEAssinar(token, sala.IdDaSala), cancelar).ConfigureAwait(false);
                while (true)
                {
                    using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
                    prazo.CancelAfter(_tempoSemNada);
                    string? quadro = await socket.ReceberAsync(prazo.Token).ConfigureAwait(false);
                    if (quadro == null)
                    {
                        _log.LogInformation("Kick: o chat de {Canal} fechou a conexão ({Motivo}).", canal, socket.Fechamento);
                        return (EstadoDaKick.SemConexao, conectou);
                    }

                    foreach (LinhaDaKick linha in QuadrosDaKick.Ler(quadro))
                    {
                        switch (linha.Tipo)
                        {
                            case TipoDeLinhaDaKick.Ping:
                                await Enviar(socket, envio, QuadrosDaKick.Pong, cancelar).ConfigureAwait(false);
                                break;
                            case TipoDeLinhaDaKick.Conectou:
                                _ = RenovarAsync(socket, envio, cliente, linha.Ttl, fim.Token);
                                break;
                            case TipoDeLinhaDaKick.Assinou:
                                conectou = true;
                                _log.LogInformation("Kick: lendo o chat da sala {Sala}.", sala.IdDaSala);
                                Mudar(EstadoDaKick.Conectado, leitura);
                                break;
                            case TipoDeLinhaDaKick.Erro when linha.Pedido is QuadrosDaKick.PedidoConectar or QuadrosDaKick.PedidoAssinar:
                                _log.LogInformation("Kick: o chat de {Canal} recusou o pedido {Pedido}: {Codigo} {Erro}", canal, linha.Pedido,
                                    linha.Codigo, linha.Texto);
                                return (EstadoDaKick.SemConexao, conectou);
                            case TipoDeLinhaDaKick.Erro:
                                _log.LogInformation("Kick: o token novo não valeu: {Codigo} {Erro}", linha.Codigo, linha.Texto);
                                break;
                            case TipoDeLinhaDaKick.Desconectar:
                                _log.LogInformation("Kick: o chat de {Canal} pediu para desconectar: {Codigo} {Motivo}", canal, linha.Codigo, linha.Texto);
                                return (EstadoDaKick.SemConexao, conectou);
                            case TipoDeLinhaDaKick.Evento:
                                Receber(linha, sala, leitura);
                                break;
                        }
                    }
                }
            }
            catch (Exception e) when (!cancelar.IsCancellationRequested)
            {
                _log.LogInformation("Kick: a conexão com o chat de {Canal} caiu: {Erro}", canal, e.Message);
                return (EstadoDaKick.SemConexao, conectou);
            }
            finally
            {
                fim.Cancel(); // para a troca do token
            }
        }
    }

    // Troca o token antes de vencer, com um novo do mesmo cliente (o de outro cliente derruba a conexão), até a leitura
    // acabar. Se a troca falhar, a Kick acaba fechando e o leitor conecta de novo do zero
    private async Task RenovarAsync(ISocketDaKick socket, SemaphoreSlim envio, string cliente, int ttl, CancellationToken fim)
    {
        TimeSpan vida = TimeSpan.FromSeconds(ttl > 0 ? ttl : 2700);
        TimeSpan intervalo = _renovarACada ?? (vida > MargemDoToken * 2 ? vida - MargemDoToken : vida / 2);
        int pedido = QuadrosDaKick.PedidoAssinar;
        try
        {
            while (true)
            {
                await Task.Delay(intervalo, fim).ConfigureAwait(false);
                try
                {
                    string token = await _kick.TokenAsync(cliente, fim).ConfigureAwait(false);
                    await Enviar(socket, envio, QuadrosDaKick.Renovar(++pedido, token), fim).ConfigureAwait(false);
                }
                catch (Exception e) when (!fim.IsCancellationRequested)
                {
                    _log.LogInformation("Kick: não deu para trocar o token: {Erro}", e.Message);
                }
            }
        }
        catch (OperationCanceledException) when (fim.IsCancellationRequested)
        {
        }
    }

    // O WebSocket aceita um envio de cada vez (os pongs e a troca do token se cruzam)
    private static async Task Enviar(ISocketDaKick socket, SemaphoreSlim envio, string quadro, CancellationToken cancelar)
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

    private void Receber(LinhaDaKick linha, SalaDaKick sala, CancellationTokenSource leitura)
    {
        EventoDaKick? evento;
        try
        {
            evento = EventosDaKick.Ler(linha.Evento, linha.Dados, sala);
        }
        catch (JsonException e)
        {
            _log.LogInformation("Kick: um evento {Evento} não foi entendido: {Erro}", linha.Evento, e.Message);
            return;
        }
        if (evento == null)
            return;
        lock (_trava)
        {
            if (leitura != _leitura)
                return;
        }
        EventoRecebido?.Invoke(evento);
    }

    // Sem leitura vale sempre (Desligar); com leitura, só se ela ainda for a atual
    private void Mudar(EstadoDaKick estado, CancellationTokenSource? leitura = null)
    {
        lock (_trava)
        {
            if ((leitura != null && leitura != _leitura) || Estado == estado)
                return;
            Estado = estado;
        }
        EstadoMudou?.Invoke(estado);
    }
}
