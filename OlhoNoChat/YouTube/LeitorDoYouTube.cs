using System.Net.Http;
using Microsoft.Extensions.Logging;
using YTLiveChat.Contracts;
using YTLiveChat.Contracts.Models;
using YTLiveChat.Contracts.Services;
using YTLiveChat.Services;

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
/// Lê o chat de uma live do YouTube, só leitura, pela biblioteca YTLiveChat (a mesma API interna que a página do YouTube
/// usa; sem chave nem conta). Quando o canal não está ao vivo, ou a live acaba, espera e procura de novo; quando o canal
/// não existe, para. Os avisos chegam em outra thread (quem usa passa para a da tela).
/// </summary>
public sealed class LeitorDoYouTube : IDisposable
{
    /// <summary>Entre uma procura e outra quando o canal não está ao vivo.</summary>
    public static readonly TimeSpan EsperaPorLive = TimeSpan.FromSeconds(60);

    /// <summary>Entre uma tentativa e outra quando o YouTube não respondeu.</summary>
    public static readonly TimeSpan EsperaSemConexao = TimeSpan.FromSeconds(20);

    /// <summary>
    /// De quanto em quanto tempo pede as mensagens novas. Um pouco mais devagar que o padrão da biblioteca (1 s): menos
    /// pedidos ao YouTube, e as mensagens chegam juntas do mesmo jeito.
    /// </summary>
    public const int IntervaloDosPedidosMs = 2000;

    /// <summary>
    /// Ao entrar no chat de uma live, o YouTube manda junto as últimas mensagens já enviadas. Nos primeiros segundos depois
    /// de conectar, as mensagens enviadas antes da conexão (com esta folga para o relógio do PC) não aparecem.
    /// </summary>
    public static readonly TimeSpan JanelaDoHistorico = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan FolgaDoRelogio = TimeSpan.FromSeconds(5);

    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient { BaseAddress = new Uri("https://www.youtube.com") });

    private readonly Func<IYTLiveChat> _criarChat;
    private readonly ILogger _log;
    private readonly TimeSpan _esperaPorLive;
    private readonly TimeSpan _esperaSemConexao;
    private readonly object _trava = new();
    private IYTLiveChat? _chat;
    private CancellationTokenSource? _proximaTentativa;
    private string _ultimoErro = string.Empty;
    private DateTimeOffset _conectadoEm;

    public LeitorDoYouTube(ILogger<LeitorDoYouTube> log)
        : this(log, CriarChatDaBiblioteca, EsperaPorLive, EsperaSemConexao)
    {
    }

    /// <param name="criarChat">Um leitor novo da biblioteca a cada tentativa (os testes usam um falso).</param>
    public LeitorDoYouTube(ILogger log, Func<IYTLiveChat> criarChat, TimeSpan esperaPorLive, TimeSpan esperaSemConexao)
    {
        _log = log;
        _criarChat = criarChat;
        _esperaPorLive = esperaPorLive;
        _esperaSemConexao = esperaSemConexao;
    }

    /// <summary>O estado mudou (em outra thread).</summary>
    public event Action<EstadoDoYouTube>? EstadoMudou;

    /// <summary>Uma mensagem do chat (em outra thread).</summary>
    public event Action<ChatItem>? MensagemRecebida;

    public CanalDoYouTube? Canal { get; private set; }

    public EstadoDoYouTube Estado { get; private set; }

    /// <summary>Mostrar o histórico que o YouTube manda ao conectar (opção "Mostrar as mensagens de antes de conectar").</summary>
    public bool MostrarHistorico { get; set; }

    private static IYTLiveChat CriarChatDaBiblioteca() =>
        new YTLiveChat.Services.YTLiveChat(new YTLiveChatOptions { RequestFrequency = IntervaloDosPedidosMs }, new YTHttpClient(Http.Value));

    /// <summary>
    /// Começa a ler o chat do canal. O mesmo canal já ligado não recomeça (as mensagens continuam); um canal que não
    /// existia é procurado de novo.
    /// </summary>
    public void Ligar(CanalDoYouTube canal)
    {
        lock (_trava)
        {
            if (canal == Canal && Estado is not (EstadoDoYouTube.Desligado or EstadoDoYouTube.CanalNaoExiste))
                return;
            Parar();
            Canal = canal;
            _log.LogInformation("YouTube: lendo o chat de {Canal}.", canal);
        }
        Abrir();
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

    // Com a trava
    private void Parar()
    {
        _proximaTentativa?.Cancel();
        _proximaTentativa?.Dispose();
        _proximaTentativa = null;
        IYTLiveChat? chat = _chat;
        _chat = null;
        chat?.Dispose(); // só pede para parar; não espera
    }

    private void Abrir()
    {
        IYTLiveChat chat;
        CanalDoYouTube? canal;
        lock (_trava)
        {
            canal = Canal;
            if (canal == null)
                return;
            chat = _criarChat();
            _chat = chat;
            _ultimoErro = string.Empty;
        }

        // Avisos de um leitor antigo (de antes de trocar ou parar) são ignorados
        chat.InitialPageLoaded += (_, _) =>
        {
            if (!EhOAtual(chat))
                return;
            _conectadoEm = DateTimeOffset.UtcNow;
            Mudar(EstadoDoYouTube.Conectado);
        };
        chat.ChatReceived += (_, e) =>
        {
            if (EhOAtual(chat) && (MostrarHistorico || !EhDoHistorico(e.ChatItem.Timestamp, _conectadoEm, DateTimeOffset.UtcNow)))
                MensagemRecebida?.Invoke(e.ChatItem);
        };
        chat.ErrorOccurred += (_, e) =>
        {
            lock (_trava)
            {
                if (chat == _chat)
                    _ultimoErro = e.GetException().Message;
            }
        };
        chat.ChatStopped += (_, e) => Parou(chat, e.Reason);

        Mudar(EstadoDoYouTube.Procurando);
        switch (canal.Tipo)
        {
            case TipoDeCanalDoYouTube.Arroba:
                chat.Start(handle: "@" + canal.Valor);
                break;
            case TipoDeCanalDoYouTube.Id:
                chat.Start(channelId: canal.Valor);
                break;
            default:
                chat.Start(liveId: canal.Valor);
                break;
        }
    }

    private bool EhOAtual(IYTLiveChat chat)
    {
        lock (_trava)
            return chat == _chat;
    }

    // A leitura parou sozinha: canal fora do ar, live acabou, canal que não existe ou falha de conexão
    private void Parou(IYTLiveChat chat, string? motivo)
    {
        EstadoDoYouTube novo;
        lock (_trava)
        {
            if (chat != _chat)
                return;
            _chat = null;
            chat.Dispose();

            novo = PorQueParou(motivo + " " + _ultimoErro);
            _log.LogInformation("YouTube: a leitura parou ({Estado}): {Motivo} {Erro}", novo, motivo, _ultimoErro);
            if (novo != EstadoDoYouTube.CanalNaoExiste)
                TentarDeNovoDepois(novo == EstadoDoYouTube.SemConexao ? _esperaSemConexao : _esperaPorLive);
        }
        Mudar(novo);
    }

    /// <summary>
    /// Uma mensagem que chegou logo depois de conectar mas foi enviada antes: o histórico que o YouTube manda ao entrar.
    /// Depois da <see cref="JanelaDoHistorico"/> tudo aparece (um relógio do PC muito errado não esconde mensagens novas).
    /// </summary>
    public static bool EhDoHistorico(DateTimeOffset enviadaEm, DateTimeOffset conectadoEm, DateTimeOffset agora) =>
        agora - conectadoEm < JanelaDoHistorico && enviadaEm < conectadoEm - FolgaDoRelogio;

    /// <summary>O que o motivo da parada (e o último erro) quer dizer.</summary>
    public static EstadoDoYouTube PorQueParou(string texto)
    {
        if (texto.Contains("404", StringComparison.Ordinal))
            return EstadoDoYouTube.CanalNaoExiste;

        string[] semLive =
        [
            "Live Stream ID not found", "canonical link not found", "is finished live", "Continuation token not found",
            "No acceptable livestream", "Stream ended", "continuation lost",
        ];
        return semLive.Any(t => texto.Contains(t, StringComparison.OrdinalIgnoreCase))
            ? EstadoDoYouTube.EsperandoALive
            : EstadoDoYouTube.SemConexao;
    }

    // Com a trava
    private void TentarDeNovoDepois(TimeSpan espera)
    {
        var cancelar = new CancellationTokenSource();
        _proximaTentativa = cancelar;
        _ = Task.Delay(espera, cancelar.Token).ContinueWith(tarefa =>
        {
            lock (_trava)
            {
                if (tarefa.IsCanceled || _proximaTentativa != cancelar)
                    return;
                _proximaTentativa = null;
                cancelar.Dispose();
            }
            Abrir();
        }, TaskScheduler.Default);
    }

    private void Mudar(EstadoDoYouTube estado)
    {
        lock (_trava)
        {
            if (Estado == estado)
                return;
            Estado = estado;
        }
        EstadoMudou?.Invoke(estado);
    }
}
