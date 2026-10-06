using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.YouTube;

/// <summary>Como está a leitura do chat do YouTube (o ponto do YouTube na faixa do canal).</summary>
public enum EstadoDoYouTube
{
    /// <summary>Sem Chat Multiplataforma ou sem canal do YouTube.</summary>
    Desligado,

    /// <summary>Procurando a live do canal.</summary>
    Procurando,

    /// <summary>Lendo o chat da live.</summary>
    Conectado,

    /// <summary>O canal não está ao vivo (ou a live acabou): tenta de novo de tempos em tempos.</summary>
    EsperandoALive,

    /// <summary>O YouTube disse que o canal não existe: não tenta de novo até trocar o canal.</summary>
    CanalNaoExiste,

    /// <summary>Sem internet ou o YouTube não respondeu: tenta de novo daqui a pouco.</summary>
    SemConexao,
}

/// <summary>
/// Lê o chat de uma live do YouTube, só leitura, pela API interna que a própria página do YouTube usa (InnerTube; sem
/// chave nem conta): abre a página da live do canal e depois pede as mensagens novas de tempos em tempos. Quando o canal
/// não está ao vivo, ou a live acaba, espera e procura de novo; quando o canal não existe, para. Os avisos chegam em
/// outra thread (quem usa passa para a da tela).
/// </summary>
public sealed class LeitorDoYouTube : IDisposable
{
    /// <summary>Entre uma procura e outra quando o canal não está ao vivo.</summary>
    public static readonly TimeSpan EsperaPorLive = TimeSpan.FromSeconds(60);

    /// <summary>Entre uma tentativa e outra quando o YouTube não respondeu.</summary>
    public static readonly TimeSpan EsperaSemConexao = TimeSpan.FromSeconds(20);

    /// <summary>
    /// De quanto em quanto tempo pede as mensagens novas (ou mais devagar, se o YouTube pedir): poucos pedidos ao
    /// YouTube, e as mensagens chegam juntas do mesmo jeito.
    /// </summary>
    public const int IntervaloDosPedidosMs = 2000;

    /// <summary>
    /// Quantas vezes seguidas o pedido das mensagens pode falhar (esperando um pouco mais a cada vez) antes de desistir da
    /// live e ir para <see cref="EstadoDoYouTube.SemConexao"/>.
    /// </summary>
    public const int MaximoDeFalhas = 5;

    /// <summary>
    /// Ao entrar no chat de uma live, o YouTube manda junto as últimas mensagens já enviadas. Nos primeiros segundos depois
    /// de conectar, as mensagens enviadas antes da conexão (com esta folga para o relógio do PC) não aparecem.
    /// </summary>
    public static readonly TimeSpan JanelaDoHistorico = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan FolgaDoRelogio = TimeSpan.FromSeconds(5);

    private readonly IConexaoComOYouTube _youTube;
    private readonly ILogger _log;
    private readonly TimeSpan _esperaPorLive;
    private readonly TimeSpan _esperaSemConexao;
    private readonly TimeSpan _intervalo;
    private readonly object _trava = new();
    // A leitura atual; trocar de canal ou parar a cancela, e os avisos de uma leitura antiga são ignorados
    private CancellationTokenSource? _leitura;

    public LeitorDoYouTube(ILogger<LeitorDoYouTube> log)
        : this(log, new ConexaoComOYouTube(), EsperaPorLive, EsperaSemConexao, TimeSpan.FromMilliseconds(IntervaloDosPedidosMs))
    {
    }

    /// <param name="youTube">Os pedidos ao YouTube (os testes usam um falso).</param>
    /// <param name="intervalo">Entre um pedido das mensagens e outro.</param>
    public LeitorDoYouTube(ILogger log, IConexaoComOYouTube youTube, TimeSpan esperaPorLive, TimeSpan esperaSemConexao, TimeSpan intervalo)
    {
        _log = log;
        _youTube = youTube;
        _esperaPorLive = esperaPorLive;
        _esperaSemConexao = esperaSemConexao;
        _intervalo = intervalo;
    }

    /// <summary>O estado mudou (em outra thread).</summary>
    public event Action<EstadoDoYouTube>? EstadoMudou;

    /// <summary>Uma mensagem do chat (em outra thread).</summary>
    public event Action<ItemDoChat>? MensagemRecebida;

    public CanalDoYouTube? Canal { get; private set; }

    public EstadoDoYouTube Estado { get; private set; }

    /// <summary>Mostrar o histórico que o YouTube manda ao conectar (opção "Mostrar as mensagens de antes de conectar").</summary>
    public bool MostrarHistorico { get; set; }

    /// <summary>
    /// Começa a ler o chat do canal. O mesmo canal já ligado não recomeça (as mensagens continuam); um canal que não
    /// existia é procurado de novo.
    /// </summary>
    public void Ligar(CanalDoYouTube canal)
    {
        var leitura = new CancellationTokenSource();
        lock (_trava)
        {
            if (canal == Canal && Estado is not (EstadoDoYouTube.Desligado or EstadoDoYouTube.CanalNaoExiste))
                return;
            Parar();
            Canal = canal;
            _leitura = leitura;
            _log.LogInformation("YouTube: lendo o chat de {Canal}.", canal);
        }
        Mudar(EstadoDoYouTube.Procurando, leitura);
        _ = Task.Run(() => LerAsync(canal, leitura));
    }

    /// <summary>Para de ler (Chat Multiplataforma desligado, canal do YouTube apagado ou o app fechando).</summary>
    public void Desligar()
    {
        lock (_trava)
        {
            if (Canal == null && Estado == EstadoDoYouTube.Desligado)
                return;
            Parar();
            Canal = null;
        }
        Mudar(EstadoDoYouTube.Desligado);
    }

    public void Dispose() => Desligar();

    // Com a trava. Só pede para parar; não espera (sem prazo marcado, o CancellationTokenSource dispensa o Dispose)
    private void Parar()
    {
        _leitura?.Cancel();
        _leitura = null;
    }

    // Procura a live, lê o chat até ela acabar e espera para procurar de novo; até cancelar ou o canal não existir
    private async Task LerAsync(CanalDoYouTube canal, CancellationTokenSource leitura)
    {
        CancellationToken cancelar = leitura.Token;
        try
        {
            while (true)
            {
                EstadoDoYouTube parou = await LerUmaVezAsync(canal, leitura).ConfigureAwait(false);
                Mudar(parou, leitura);
                if (parou == EstadoDoYouTube.CanalNaoExiste)
                    return;
                await Task.Delay(parou == EstadoDoYouTube.SemConexao ? _esperaSemConexao : _esperaPorLive, cancelar).ConfigureAwait(false);
                Mudar(EstadoDoYouTube.Procurando, leitura);
            }
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
        }
    }

    // Uma procura; devolve por que parou: canal fora do ar, live acabou, canal que não existe ou falha de conexão
    private async Task<EstadoDoYouTube> LerUmaVezAsync(CanalDoYouTube canal, CancellationTokenSource leitura)
    {
        CancellationToken cancelar = leitura.Token;
        PaginaDaLive? pagina;
        try
        {
            pagina = PaginaDaLive.Ler(await _youTube.PaginaAsync(PaginaDaLive.Caminho(canal), cancelar).ConfigureAwait(false));
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            _log.LogInformation("YouTube: o canal {Canal} não existe.", canal);
            return EstadoDoYouTube.CanalNaoExiste;
        }
        catch (Exception e) when (!cancelar.IsCancellationRequested)
        {
            _log.LogInformation("YouTube: a página de {Canal} não abriu: {Erro}", canal, e.Message);
            return EstadoDoYouTube.SemConexao;
        }
        if (pagina == null)
        {
            _log.LogInformation("YouTube: {Canal} não está ao vivo.", canal);
            return EstadoDoYouTube.EsperandoALive;
        }

        DateTimeOffset conectadoEm = DateTimeOffset.UtcNow;
        _log.LogInformation("YouTube: lendo o chat da live {Live}.", pagina.IdDaLive);
        Mudar(EstadoDoYouTube.Conectado, leitura);

        string? continuacao = pagina.Continuacao;
        int falhas = 0;
        while (continuacao != null)
        {
            RespostaDoChat resposta;
            try
            {
                string json = await _youTube.ChatAsync(pagina.Chave, pagina.PedidoDoChat(continuacao), cancelar).ConfigureAwait(false);
                resposta = RespostaDoChat.Ler(json);
                falhas = 0;
            }
            catch (Exception e) when (!cancelar.IsCancellationRequested)
            {
                // 403: live só para membros ou bloqueada no país; não adianta insistir
                if (e is HttpRequestException { StatusCode: HttpStatusCode.Forbidden } || ++falhas > MaximoDeFalhas)
                {
                    _log.LogInformation("YouTube: o chat da live {Live} não respondeu: {Erro}", pagina.IdDaLive, e.Message);
                    return EstadoDoYouTube.SemConexao;
                }
                await Task.Delay(_intervalo * falhas, cancelar).ConfigureAwait(false);
                continue;
            }

            foreach (ItemDoChat item in resposta.Itens)
            {
                if (MostrarHistorico || !EhDoHistorico(item.EnviadaEm, conectadoEm, DateTimeOffset.UtcNow))
                    Receber(item, leitura);
            }
            continuacao = resposta.Continuacao;
            if (continuacao != null)
                await Task.Delay(resposta.Espera > _intervalo ? resposta.Espera.Value : _intervalo, cancelar).ConfigureAwait(false);
        }
        _log.LogInformation("YouTube: a live {Live} acabou.", pagina.IdDaLive);
        return EstadoDoYouTube.EsperandoALive;
    }

    private void Receber(ItemDoChat item, CancellationTokenSource leitura)
    {
        lock (_trava)
        {
            if (leitura != _leitura)
                return;
        }
        MensagemRecebida?.Invoke(item);
    }

    /// <summary>
    /// Uma mensagem que chegou logo depois de conectar mas foi enviada antes: o histórico que o YouTube manda ao entrar.
    /// Depois da <see cref="JanelaDoHistorico"/> tudo aparece (um relógio do PC muito errado não esconde mensagens novas).
    /// </summary>
    public static bool EhDoHistorico(DateTimeOffset enviadaEm, DateTimeOffset conectadoEm, DateTimeOffset agora) =>
        agora - conectadoEm < JanelaDoHistorico && enviadaEm < conectadoEm - FolgaDoRelogio;

    // Sem leitura vale sempre (Desligar); com leitura, só se ela ainda for a atual
    private void Mudar(EstadoDoYouTube estado, CancellationTokenSource? leitura = null)
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
