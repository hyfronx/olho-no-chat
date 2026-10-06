using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.YouTube;
using static OlhoNoChat.Testes.YouTube.AmostrasDoYouTube;

namespace OlhoNoChat.Testes.YouTube;

/// <summary>
/// O leitor do YouTube com um YouTube falso: quando procura de novo (canal fora do ar, live que acabou, sem conexão),
/// quando para (canal que não existe), o que pede ao YouTube e que os avisos de uma leitura antiga são ignorados.
/// </summary>
public class LeitorDoYouTubeTestes
{
    private static readonly TimeSpan Espera = TimeSpan.FromMilliseconds(50);
    private readonly YouTubeFalso _youTube = new();
    private readonly ConcurrentQueue<EstadoDoYouTube> _estados = new();
    private readonly ConcurrentQueue<string> _mensagens = new();

    private LeitorDoYouTube Novo()
    {
        var leitor = new LeitorDoYouTube(NullLogger.Instance, _youTube, Espera, Espera, TimeSpan.FromMilliseconds(10));
        leitor.EstadoMudou += estado => _estados.Enqueue(estado);
        leitor.MensagemRecebida += item => _mensagens.Enqueue(item.Id);
        return leitor;
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (int i = 0; i < 300 && !condicao(); i++)
            await Task.Delay(10);
        Assert.True(condicao());
    }

    [Fact]
    public async Task Ligar_ProcuraPeloArroba()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        Assert.Equal(EstadoDoYouTube.Procurando, leitor.Estado);
        await Esperar(() => _youTube.Caminhos.Count == 1);
        Assert.Equal("/@Hyfronx/live", _youTube.Caminhos[0]);
    }

    [Fact]
    public async Task Ligar_PeloIdOuPelaLive()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("UCSJ4gkVC6NrvII8umztf0Ow")!);
        await Esperar(() => _youTube.Caminhos.Count == 1);
        leitor.Ligar(CanalDoYouTube.Ler("youtu.be/jfKfPfyJRdk")!);
        await Esperar(() => _youTube.Caminhos.Count == 2);

        Assert.Equal(["/channel/UCSJ4gkVC6NrvII8umztf0Ow/live", "/watch?v=jfKfPfyJRdk"], _youTube.Caminhos);
        Assert.True(_youTube.Cancelamentos[0].IsCancellationRequested); // a leitura do primeiro foi cancelada
        Assert.False(_youTube.Cancelamentos[1].IsCancellationRequested);
    }

    [Fact]
    public async Task Conectado_EAsMensagensChegam()
    {
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(Resposta("cont1", Texto("m1")));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _mensagens.Count == 1);
        Assert.Equal(EstadoDoYouTube.Conectado, leitor.Estado);
        Assert.Equal(["m1"], _mensagens);
    }

    [Fact]
    public async Task PedeAsMensagens_ComAChaveEAContinuacaoDaVez()
    {
        _youTube.Pagina(PaginaComLive("cont0"));
        _youTube.Chat(Resposta("cont1"));
        _youTube.Chat(Resposta("cont2"));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _youTube.PedidosDoChat.Count == 3);
        Assert.All(_youTube.PedidosDoChat, p => Assert.Equal("chave1", p.Chave));
        Assert.Contains("\"continuation\":\"cont0\"", _youTube.PedidosDoChat[0].Corpo);
        Assert.Contains("\"continuation\":\"cont1\"", _youTube.PedidosDoChat[1].Corpo);
        Assert.Contains("\"continuation\":\"cont2\"", _youTube.PedidosDoChat[2].Corpo);
    }

    [Fact]
    public async Task MesmoCanal_NaoRecomeca()
    {
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        leitor.Ligar(CanalDoYouTube.Ler("youtube.com/@Hyfronx")!);
        await Task.Delay(Espera * 2);

        Assert.Single(_youTube.Caminhos);
    }

    [Fact]
    public async Task ForaDoAr_EsperaEProcuraDeNovo()
    {
        _youTube.Pagina(PaginaSemLive);
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _estados.Contains(EstadoDoYouTube.EsperandoALive));
        await Esperar(() => _youTube.Caminhos.Count == 2 && leitor.Estado == EstadoDoYouTube.Procurando);
    }

    [Fact]
    public async Task LiveAcabou_VoltaAEsperar()
    {
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(Resposta("cont1", Texto("m1")));
        _youTube.Chat(Resposta(null));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _youTube.Caminhos.Count == 2);
        Assert.Equal([EstadoDoYouTube.Procurando, EstadoDoYouTube.Conectado, EstadoDoYouTube.EsperandoALive, EstadoDoYouTube.Procurando],
            _estados);
    }

    [Fact]
    public async Task CanalQueNaoExiste_NaoProcuraDeNovo_AteLigarOutraVez()
    {
        _youTube.Pagina(new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@naoexiste")!);

        await Esperar(() => leitor.Estado == EstadoDoYouTube.CanalNaoExiste);
        await Task.Delay(Espera * 4);
        Assert.Single(_youTube.Caminhos);

        leitor.Ligar(CanalDoYouTube.Ler("@naoexiste")!); // confirmar de novo na faixa procura de novo
        await Esperar(() => _youTube.Caminhos.Count == 2);
    }

    [Fact]
    public async Task SemConexao_TentaDeNovo()
    {
        _youTube.Pagina(new HttpRequestException("No such host is known."));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _estados.Contains(EstadoDoYouTube.SemConexao));
        await Esperar(() => _youTube.Caminhos.Count == 2);
    }

    [Fact]
    public async Task ChatFalhaUmaVez_ContinuaNaMesmaLive()
    {
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(new HttpRequestException("Erro 500", null, HttpStatusCode.InternalServerError));
        _youTube.Chat(Resposta("cont1", Texto("m1")));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _mensagens.Count == 1);
        Assert.Equal(EstadoDoYouTube.Conectado, leitor.Estado);
        Assert.Single(_youTube.Caminhos);
    }

    [Fact]
    public async Task ChatSempreFalhando_DesisteEProcuraDeNovo()
    {
        _youTube.Pagina(PaginaComLive());
        for (int i = 0; i <= LeitorDoYouTube.MaximoDeFalhas; i++)
            _youTube.Chat(new TaskCanceledException("A task was canceled."));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _estados.Contains(EstadoDoYouTube.SemConexao));
        Assert.Equal(LeitorDoYouTube.MaximoDeFalhas + 1, _youTube.PedidosDoChat.Count);
        await Esperar(() => _youTube.Caminhos.Count == 2);
    }

    [Fact]
    public async Task ChatProibido_DesisteNaHora()
    {
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(new HttpRequestException("403 (Forbidden)", null, HttpStatusCode.Forbidden));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _estados.Contains(EstadoDoYouTube.SemConexao));
        Assert.Single(_youTube.PedidosDoChat);
    }

    [Fact]
    public async Task Desligar_ParaEIgnoraOsAvisosDoAntigo()
    {
        var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);
        await Esperar(() => _youTube.Caminhos.Count == 1);
        leitor.Desligar();

        Assert.Equal(EstadoDoYouTube.Desligado, leitor.Estado);
        Assert.True(_youTube.Cancelamentos[0].IsCancellationRequested);
        // A página e as mensagens chegam atrasadas, depois de desligar
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(Resposta("cont1", Texto("atrasada")));
        await Task.Delay(Espera * 4);

        Assert.Empty(_mensagens);
        Assert.Equal(EstadoDoYouTube.Desligado, leitor.Estado);
        Assert.Single(_youTube.Caminhos);
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
    public async Task HistoricoAoConectar_NaoAparece()
    {
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(Resposta("cont1", Texto("antiga", enviadaEm: DateTimeOffset.UtcNow.AddMinutes(-2)), Texto("nova")));
        using var leitor = Novo();
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _mensagens.Count == 1);
        await Task.Delay(Espera);
        Assert.Equal(["nova"], _mensagens);
    }

    [Fact]
    public async Task HistoricoAoConectar_ComAOpcao_Aparece()
    {
        _youTube.Pagina(PaginaComLive());
        _youTube.Chat(Resposta("cont1", Texto("antiga", enviadaEm: DateTimeOffset.UtcNow.AddMinutes(-2))));
        using var leitor = Novo();
        leitor.MostrarHistorico = true;
        leitor.Ligar(CanalDoYouTube.Ler("@Hyfronx")!);

        await Esperar(() => _mensagens.Count == 1);
        Assert.Equal(["antiga"], _mensagens);
    }

    /// <summary>
    /// Responde os pedidos com o que o teste pôs na fila (texto ou erro), na ordem. Com a fila vazia, o pedido fica
    /// esperando a próxima resposta, mesmo depois de cancelado: assim dá para simular uma resposta atrasada.
    /// </summary>
    private sealed class YouTubeFalso : IConexaoComOYouTube
    {
        private readonly object _trava = new();
        private readonly Queue<object> _paginas = new(), _chats = new();
        private readonly List<string> _caminhos = [];
        private readonly List<CancellationToken> _cancelamentos = [];
        private readonly List<(string, string)> _pedidosDoChat = [];
        private TaskCompletionSource _chegou = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Caminhos { get { lock (_trava) return [.. _caminhos]; } }
        public IReadOnlyList<CancellationToken> Cancelamentos { get { lock (_trava) return [.. _cancelamentos]; } }
        public IReadOnlyList<(string Chave, string Corpo)> PedidosDoChat { get { lock (_trava) return [.. _pedidosDoChat]; } }

        public void Pagina(object resposta) => Por(_paginas, resposta);

        public void Chat(object resposta) => Por(_chats, resposta);

        public Task<string> PaginaAsync(string caminho, CancellationToken cancelar)
        {
            lock (_trava)
            {
                _caminhos.Add(caminho);
                _cancelamentos.Add(cancelar);
            }
            return Proxima(_paginas);
        }

        public Task<string> ChatAsync(string chave, string corpo, CancellationToken cancelar)
        {
            lock (_trava)
                _pedidosDoChat.Add((chave, corpo));
            return Proxima(_chats);
        }

        private void Por(Queue<object> fila, object resposta)
        {
            TaskCompletionSource chegou;
            lock (_trava)
            {
                fila.Enqueue(resposta);
                chegou = _chegou;
                _chegou = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            chegou.SetResult();
        }

        private async Task<string> Proxima(Queue<object> fila)
        {
            while (true)
            {
                Task chegou;
                lock (_trava)
                {
                    if (fila.TryDequeue(out object? resposta))
                        return resposta is Exception erro ? throw erro : (string)resposta;
                    chegou = _chegou.Task;
                }
                await chegou;
            }
        }
    }
}
