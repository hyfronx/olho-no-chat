using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.YouTube;
using YTLiveChat.Contracts.Models;
using YTLiveChat.Contracts.Services;

namespace OlhoNoChat.Testes.YouTube;

/// <summary>
/// O leitor do YouTube com um leitor falso da biblioteca: quando procura de novo (canal fora do ar, live que acabou, sem
/// conexão), quando para (canal que não existe) e que os avisos de um leitor antigo são ignorados.
/// </summary>
public class LeitorDoYouTubeTestes
{
    private static readonly TimeSpan Espera = TimeSpan.FromMilliseconds(50);
    private readonly List<ChatFalso> _criados = [];
    private readonly ConcurrentQueue<EstadoDoYouTube> _estados = new();
    private readonly ConcurrentQueue<string> _mensagens = new();

    private LeitorDoYouTube Novo()
    {
        var leitor = new LeitorDoYouTube(NullLogger.Instance, () =>
        {
            var chat = new ChatFalso();
            lock (_criados)
                _criados.Add(chat);
            return chat;
        }, Espera, Espera);
        leitor.EstadoMudou += estado => _estados.Enqueue(estado);
        leitor.MensagemRecebida += item => _mensagens.Enqueue(item.Id);
        return leitor;
    }

    private ChatFalso Criado(int indice)
    {
        lock (_criados)
            return _criados[indice];
    }

    private int QuantosCriados()
    {
        lock (_criados)
            return _criados.Count;
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (int i = 0; i < 200 && !condicao(); i++)
            await Task.Delay(10);
        Assert.True(condicao());
    }

    [Fact]
    public void Ligar_ProcuraPeloArroba()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        Assert.Equal(EstadoDoYouTube.Procurando, leitor.Estado);
        Assert.Equal(("@Hyfronx", null, null), Criado(0).Inicio);
    }

    [Fact]
    public void Ligar_PeloIdOuPelaLive()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("UCSJ4gkVC6NrvII8umztf0Ow")!);
        leitor.Ligar(CanalDoYouTube.Ler("youtu.be/jfKfPfyJRdk")!);

        Assert.Equal((null, "UCSJ4gkVC6NrvII8umztf0Ow", null), Criado(0).Inicio);
        Assert.True(Criado(0).Descartado);
        Assert.Equal((null, null, "jfKfPfyJRdk"), Criado(1).Inicio);
    }

    [Fact]
    public void Conectado_EAsMensagensChegam()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        Criado(0).Carregou();
        Criado(0).Mensagem("m1");

        Assert.Equal(EstadoDoYouTube.Conectado, leitor.Estado);
        Assert.Equal(["m1"], _mensagens);
    }

    [Fact]
    public void MesmoCanal_NaoRecomeca()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        leitor.Ligar(CanalDoYouTube.Ler("youtube.com/@Hyfronx")!);

        Assert.Equal(1, QuantosCriados());
    }

    [Fact]
    public async Task ForaDoAr_EsperaEProcuraDeNovo()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        Criado(0).Falhou("Failed to initialize from YouTube page: Live Stream ID not found");

        Assert.Equal(EstadoDoYouTube.EsperandoALive, leitor.Estado);
        Assert.True(Criado(0).Descartado);
        await Esperar(() => QuantosCriados() == 2 && leitor.Estado == EstadoDoYouTube.Procurando);
    }

    [Fact]
    public async Task LiveAcabou_VoltaAEsperar()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        Criado(0).Carregou();
        Criado(0).Parou("Stream ended or continuation lost");

        Assert.Equal(EstadoDoYouTube.EsperandoALive, leitor.Estado);
        await Esperar(() => QuantosCriados() == 2);
    }

    [Fact]
    public async Task CanalQueNaoExiste_NaoProcuraDeNovo_AteLigarOutraVez()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@naoexiste")!);
        Criado(0).Falhou("Failed to initialize from YouTube page: Response status code does not indicate success: 404 (Not Found).");

        Assert.Equal(EstadoDoYouTube.CanalNaoExiste, leitor.Estado);
        await Task.Delay(Espera * 4);
        Assert.Equal(1, QuantosCriados());

        leitor.Ligar(CanalDoYouTube.Ler("@naoexiste")!); // confirmar de novo na faixa procura de novo
        Assert.Equal(2, QuantosCriados());
    }

    [Fact]
    public async Task SemConexao_TentaDeNovo()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        Criado(0).Falhou("No such host is known.");

        Assert.Equal(EstadoDoYouTube.SemConexao, leitor.Estado);
        await Esperar(() => QuantosCriados() == 2);
    }

    [Fact]
    public async Task Desligar_ParaEIgnoraOsAvisosDoAntigo()
    {
        var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        ChatFalso antigo = Criado(0);
        leitor.Desligar();

        Assert.Equal(EstadoDoYouTube.Desligado, leitor.Estado);
        Assert.True(antigo.Descartado);
        antigo.Mensagem("atrasada");
        antigo.Falhou("Live Stream ID not found");
        await Task.Delay(Espera * 4);

        Assert.Empty(_mensagens);
        Assert.Equal(EstadoDoYouTube.Desligado, leitor.Estado);
        Assert.Equal(1, QuantosCriados());
    }

    [Theory]
    [InlineData(-60, 2, true)]   // enviada um minuto antes de conectar, chegou logo depois: histórico
    [InlineData(-3, 2, false)]   // dentro da folga do relógio: aparece
    [InlineData(1, 2, false)]    // enviada depois de conectar
    [InlineData(-60, 30, false)] // passou a janela do histórico: tudo aparece (relógio do PC errado não esconde nada)
    public void EhDoHistorico(int enviadaSegundos, int agoraSegundos, bool historico)
    {
        var conectado = new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal(historico, LeitorDoYouTube.EhDoHistorico(conectado.AddSeconds(enviadaSegundos), conectado,
            conectado.AddSeconds(agoraSegundos)));
    }

    [Fact]
    public void HistoricoAoConectar_NaoAparece()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        Criado(0).Carregou();
        Criado(0).Mensagem("antiga", DateTimeOffset.UtcNow.AddMinutes(-2));
        Criado(0).Mensagem("nova");

        Assert.Equal(["nova"], _mensagens);
    }

    [Fact]
    public void HistoricoAoConectar_ComAOpcao_Aparece()
    {
        using var leitor = Novo();
        leitor.MostrarHistorico = true;
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        Criado(0).Carregou();
        Criado(0).Mensagem("antiga", DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.Equal(["antiga"], _mensagens);
    }

    [Theory]
    [InlineData("Critical error:Failed to initialize from YouTube page: Live Stream ID not found", EstadoDoYouTube.EsperandoALive)]
    [InlineData("Stream ended or continuation lost", EstadoDoYouTube.EsperandoALive)]
    [InlineData("x 404 (Not Found)", EstadoDoYouTube.CanalNaoExiste)]
    [InlineData("A task was canceled.", EstadoDoYouTube.SemConexao)]
    public void PorQueParou(string motivo, EstadoDoYouTube estado) => Assert.Equal(estado, LeitorDoYouTube.PorQueParou(motivo));

#pragma warning disable CS0067, CS0618 // eventos da interface que o leitor não usa
    private sealed class ChatFalso : IYTLiveChat
    {
        public (string? Handle, string? ChannelId, string? LiveId)? Inicio { get; private set; }
        public bool Descartado { get; private set; }

        public event EventHandler<InitialPageLoadedEventArgs>? InitialPageLoaded;
        public event EventHandler<ChatStoppedEventArgs>? ChatStopped;
        public event EventHandler<ChatReceivedEventArgs>? ChatReceived;
        public event EventHandler<LivestreamStartedEventArgs>? LivestreamStarted;
        public event EventHandler<LivestreamEndedEventArgs>? LivestreamEnded;
        public event EventHandler<LivestreamInaccessibleEventArgs>? LivestreamInaccessible;
        public event EventHandler<RawActionReceivedEventArgs>? RawActionReceived;
        public event EventHandler<PollUpdatedEventArgs>? PollUpdated;
        public event EventHandler<PollClosedEventArgs>? PollClosed;
        public event EventHandler<ChatItemDeletedEventArgs>? ChatItemDeleted;
        public event EventHandler<ChatItemsDeletedByAuthorEventArgs>? ChatItemsDeletedByAuthor;
        public event EventHandler<BannerAddedEventArgs>? BannerAdded;
        public event EventHandler<BannerRemovedEventArgs>? BannerRemoved;
        public event EventHandler<ChatItemReplacedEventArgs>? ChatItemReplaced;
        public event EventHandler<EngagementMessageReceivedEventArgs>? EngagementMessageReceived;
        public event EventHandler<GiftReceivedEventArgs>? GiftReceived;
        public event EventHandler<CreatorGoalReceivedEventArgs>? CreatorGoalReceived;
        public event EventHandler<ErrorOccurredEventArgs>? ErrorOccurred;

        public void Start(string? handle = null, string? channelId = null, string? liveId = null, bool overwrite = false) =>
            Inicio = (handle, channelId, liveId);

        public void Stop()
        {
        }

        public void Dispose() => Descartado = true;

        public void Carregou() => InitialPageLoaded?.Invoke(this, new InitialPageLoadedEventArgs { LiveId = "live1" });

        public void Mensagem(string id, DateTimeOffset? enviadaEm = null) => ChatReceived?.Invoke(this, new ChatReceivedEventArgs
        {
            ChatItem = new ChatItem
            {
                Id = id, Author = new Author { Name = "@viewer1", ChannelId = "UC1" }, Message = [],
                Timestamp = enviadaEm ?? DateTimeOffset.UtcNow,
            },
        });

        // Como a biblioteca faz: o erro, depois a parada com "Critical error: ..."
        public void Falhou(string erro)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs(new InvalidOperationException(erro)));
            ChatStopped?.Invoke(this, new ChatStoppedEventArgs { Reason = "Critical error: " + erro });
        }

        public void Parou(string motivo) => ChatStopped?.Invoke(this, new ChatStoppedEventArgs { Reason = motivo });

        public Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(string? handle = null, string? channelId = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatItem> StreamChatItemsAsync(string? handle = null, string? channelId = null, string? liveId = null,
            bool overwrite = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<RawActionReceivedEventArgs> StreamRawActionsAsync(string? handle = null, string? channelId = null,
            string? liveId = null, bool overwrite = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
#pragma warning restore CS0067, CS0618
}
