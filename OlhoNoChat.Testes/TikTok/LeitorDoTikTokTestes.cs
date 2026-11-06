using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.TikTok;
using static OlhoNoChat.Testes.TikTok.AmostrasDoTikTok;

namespace OlhoNoChat.Testes.TikTok;

/// <summary>
/// O leitor do TikTok com um TikTok falso: quando procura de novo (fora do ar, live que acabou, conexão que caiu ou
/// ficou muda), quando para (perfil que não existe), o que manda no WebSocket e que as mensagens repetidas não voltam.
/// </summary>
public class LeitorDoTikTokTestes
{
    private static readonly TimeSpan Espera = TimeSpan.FromMilliseconds(50);
    private readonly TikTokFalso _tikTok = new();
    private readonly ConcurrentQueue<EstadoDoTikTok> _estados = new();
    private readonly ConcurrentQueue<string> _mensagens = new();
    private readonly ConcurrentQueue<(IReadOnlyList<string> Ids, IReadOnlyList<string> Logins)> _apagadas = new();
    private readonly ConcurrentQueue<AvisoDoTikTok> _avisos = new();

    private LeitorDoTikTok Novo(TimeSpan? batidas = null, TimeSpan? tempoSemNada = null)
    {
        var leitor = new LeitorDoTikTok(NullLogger.Instance, _tikTok, Espera, Espera, batidas ?? TimeSpan.FromSeconds(10),
            tempoSemNada ?? TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20));
        leitor.EstadoMudou += estado => _estados.Enqueue(estado);
        leitor.MensagemRecebida += item => _mensagens.Enqueue(item.Id);
        leitor.MensagensApagadas += (ids, logins) => _apagadas.Enqueue((ids, logins));
        leitor.AvisoRecebido += aviso => _avisos.Enqueue(aviso);
        return leitor;
    }

    private static async Task Esperar(Func<bool> condicao, int vezes = 300)
    {
        for (int i = 0; i < vezes && !condicao(); i++)
            await Task.Delay(10);
        Assert.True(condicao());
    }

    [Fact]
    public async Task Ligar_ProcuraASalaDoCanal()
    {
        using var leitor = Novo();
        leitor.Ligar("fulano");

        Assert.Equal(EstadoDoTikTok.Procurando, leitor.Estado);
        await Esperar(() => _tikTok.Salas.Count == 1);
        Assert.Equal("fulano", _tikTok.Salas.First());
    }

    [Fact]
    public async Task AoVivo_EntraNaSala_EAsMensagensChegam_EConfirma()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        socket.Chega(Quadro(55, MensagemDoChat(1, "viewer1", "oi")));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _mensagens.Count == 1);
        Assert.Equal(EstadoDoTikTok.Conectado, leitor.Estado);
        Assert.Equal([IdDaSala], _tikTok.Abertos);
        await Esperar(() => socket.Tipos.Count == 2);
        Assert.Equal(["im_enter_room", "ack"], socket.Tipos);
    }

    [Fact]
    public async Task Repetidas_NaoAparecemDeNovo()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        socket.Chega(Quadro(1, MensagemDoChat(1, "viewer1", "oi"), MensagemDoChat(2, "viewer2", "olá")));
        socket.Chega(Quadro(2, MensagemDoChat(2, "viewer2", "olá"), MensagemDoChat(3, "viewer3", "eae")));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _mensagens.Count == 3);
        await Task.Delay(Espera);
        Assert.Equal(["1", "2", "3"], _mensagens);
    }

    [Fact]
    public async Task MesmoCanal_NaoRecomeca()
    {
        using var leitor = Novo();
        leitor.Ligar("fulano");
        leitor.Ligar("fulano");
        await Task.Delay(Espera / 2);

        Assert.Single(_tikTok.Salas);
    }

    [Fact]
    public async Task ForaDoAr_EsperaEProcuraDeNovo()
    {
        _tikTok.Sala(Sala(aoVivo: false));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _estados.Contains(EstadoDoTikTok.EsperandoALive));
        await Esperar(() => _tikTok.Salas.Count == 2);
        Assert.Empty(_tikTok.Abertos);
    }

    [Fact]
    public async Task PerfilQueNaoExiste_NaoProcuraDeNovo_AteLigarOutraVez()
    {
        _tikTok.Sala(SalaQueNaoExiste);
        using var leitor = Novo();
        leitor.Ligar("naoexiste");

        await Esperar(() => leitor.Estado == EstadoDoTikTok.CanalNaoExiste);
        await Task.Delay(Espera * 4);
        Assert.Single(_tikTok.Salas);

        leitor.Ligar("naoexiste"); // confirmar de novo na faixa procura de novo
        await Esperar(() => _tikTok.Salas.Count == 2);
    }

    [Fact]
    public async Task LiveAcabou_FechaEVoltaAEsperar()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        socket.Chega(Quadro(1, MensagemDoChat(1, "viewer1", "tchau"), Controle(3)));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _tikTok.Salas.Count == 2);
        Assert.True(socket.Fechado);
        Assert.Equal(["1"], _mensagens);
        Assert.Equal([EstadoDoTikTok.Procurando, EstadoDoTikTok.Conectado, EstadoDoTikTok.EsperandoALive, EstadoDoTikTok.Procurando],
            _estados.Take(4));
    }

    [Fact]
    public async Task ConexaoCaiu_ConectaDeNovoSemFicarVermelho()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso primeiro = _tikTok.Socket();
        primeiro.Chega(null); // o TikTok fechou
        SocketFalso segundo = _tikTok.Socket();
        segundo.Chega(Quadro(1, MensagemDoChat(1, "viewer1", "voltei")));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _mensagens.Count == 1);
        Assert.Equal(2, _tikTok.Abertos.Count);
        Assert.DoesNotContain(EstadoDoTikTok.SemConexao, _estados);
        Assert.True(primeiro.Fechado);
    }

    [Fact]
    public async Task SemNadaDoTikTok_DerrubaEConectaDeNovo()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso mudo = _tikTok.Socket();
        _tikTok.Socket();
        using var leitor = Novo(tempoSemNada: TimeSpan.FromMilliseconds(100));
        leitor.Ligar("fulano");

        await Esperar(() => _tikTok.Abertos.Count == 2);
        Assert.True(mudo.Fechado);
    }

    [Fact]
    public async Task Batidas_DeTemposEmTempos()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        using var leitor = Novo(batidas: TimeSpan.FromMilliseconds(20));
        leitor.Ligar("fulano");

        await Esperar(() => socket.Tipos.Count(t => t == "hb") >= 2);
        Assert.Equal("im_enter_room", socket.Tipos.First());
    }

    [Fact]
    public async Task SalaNaoRespondeu_SemConexaoETentaDeNovo()
    {
        _tikTok.Sala(new HttpRequestException("sem internet"));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _estados.Contains(EstadoDoTikTok.SemConexao));
        await Esperar(() => _tikTok.Salas.Count == 2);
    }

    [Fact]
    public async Task WebSocketRecusado_SemConexao()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        using var leitor = Novo(); // sem socket na fila: o falso recusa
        leitor.Ligar("fulano");

        await Esperar(() => _estados.Contains(EstadoDoTikTok.SemConexao));
    }

    [Fact]
    public async Task Apagadas_ComOArrobaDeQuemMandou()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        socket.Chega(Quadro(1, MensagemDoChat(1001, "viewer1", "spam", idDoUsuario: 42)));
        socket.Chega(Quadro(2, Apagar([1001], [42, 999])));
        using var leitor = Novo();
        leitor.Ligar("fulano");

        await Esperar(() => _apagadas.Count == 1);
        (IReadOnlyList<string> ids, IReadOnlyList<string> logins) = _apagadas.Single();
        Assert.Equal(["1001"], ids);
        Assert.Equal(["viewer1"], logins); // o 999 nunca falou: não tem @
    }

    [Fact]
    public async Task OutroCanal_CancelaOAnterior_EIgnoraOQueEleManda()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso antigo = _tikTok.Socket();
        using var leitor = Novo();
        leitor.Ligar("fulano");
        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);

        leitor.Ligar("outro");
        antigo.Chega(Quadro(1, MensagemDoChat(1, "viewer1", "do antigo")));
        await Esperar(() => _tikTok.Salas.Count == 2);
        await Task.Delay(Espera);

        Assert.Empty(_mensagens);
        Assert.Equal("outro", leitor.Canal);
    }

    [Fact]
    public async Task Desligar_ParaTudo()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        using var leitor = Novo();
        leitor.Ligar("fulano");
        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);

        leitor.Desligar();

        Assert.Equal(EstadoDoTikTok.Desligado, leitor.Estado);
        Assert.Null(leitor.Canal);
        await Esperar(() => socket.Fechado);
    }

    [Fact]
    public async Task Presentes_UmAvisoPorCombo_ComONomeEmPortugues()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        using var leitor = Novo();
        leitor.Ligar("fulano");
        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado && _tikTok.PedidosDaLista == 1);
        await Task.Delay(Espera); // a lista chega

        socket.Chega(Quadro(1, Presente(1, "viewer1", 5655, "Rose", 1, combo: 77, contagem: 1)));
        socket.Chega(Quadro(2, Presente(2, "viewer1", 5655, "Rose", 1, combo: 77, contagem: 3)));
        socket.Chega(Quadro(3, Presente(3, "viewer1", 5655, "Rose", 1, combo: 77, contagem: 5, fim: true)));

        await Esperar(() => _avisos.Count == 1);
        AvisoDoTikTok aviso = _avisos.Single();
        Assert.Equal(TipoDeAvisoDoTikTok.Presente, aviso.Tipo);
        Assert.Equal(new PresenteDoTikTok("Rosa", ImagemDaRosa, 5, 5), aviso.Presente);
        Assert.Equal("viewer1", aviso.Login);
    }

    [Fact]
    public async Task PresenteSemCombo_SaiDepoisDoSilencio_ESeguidorNaHora()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        SocketFalso socket = _tikTok.Socket();
        using var leitor = Novo();
        leitor.Ligar("fulano");
        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);

        socket.Chega(Quadro(1, Presente(1, "viewer2", 5660, "Hand Heart", 100, tipo: 2, combo: 9, contagem: 1), Social(5, "viewer3", 1)));
        socket.Chega(Quadro(2, Presente(2, "viewer2", 5660, "Hand Heart", 100, tipo: 2, combo: 9, contagem: 1)));

        await Esperar(() => _avisos.Any(a => a.Tipo == TipoDeAvisoDoTikTok.Seguiu));
        Assert.DoesNotContain(_avisos, a => a.Tipo == TipoDeAvisoDoTikTok.Presente); // ainda esperando o silêncio
        await Esperar(() => _avisos.Any(a => a.Tipo == TipoDeAvisoDoTikTok.Presente), 600);
        Assert.Equal(new PresenteDoTikTok("Hand Heart", ImagemDaRosa, 2, 200), _avisos.Single(a => a.Presente != null).Presente);
    }

    [Fact]
    public async Task ComConta_ConectaComASessao()
    {
        _tikTok.Sessao = "sessao1";
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Socket();
        using var leitor = Novo();
        bool recusada = false;
        leitor.SessaoRecusada += () => recusada = true;
        leitor.Ligar("fulano");

        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);
        Assert.Equal(["sessao1"], _tikTok.SessoesAbertas);
        Assert.False(recusada);
    }

    [Fact]
    public async Task SessaoRecusada_Avisa_EConectaSemEla()
    {
        _tikTok.Sessao = "vencida";
        _tikTok.Situacao = SituacaoDaSessao.Recusada;
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Socket();
        using var leitor = Novo();
        // Quem usa (a conta) esquece a sessão ao ouvir o aviso
        int avisos = 0;
        leitor.SessaoRecusada += () =>
        {
            avisos++;
            _tikTok.Sessao = null;
        };
        leitor.Ligar("fulano");

        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);
        Assert.Equal(1, avisos);
        Assert.Equal([null], _tikTok.SessoesAbertas);
    }

    [Fact]
    public async Task SemResposta_DaConferencia_ContinuaComASessao()
    {
        _tikTok.Sessao = "sessao1";
        _tikTok.Situacao = SituacaoDaSessao.SemResposta;
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Socket();
        using var leitor = Novo();
        bool recusada = false;
        leitor.SessaoRecusada += () => recusada = true;
        leitor.Ligar("fulano");

        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);
        Assert.False(recusada);
        Assert.Equal(["sessao1"], _tikTok.SessoesAbertas);
    }

    [Fact]
    public async Task Recomecar_ConectaDeNovoAoMesmoCanal()
    {
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Socket();
        _tikTok.Sala(Sala(aoVivo: true));
        _tikTok.Socket();
        using var leitor = Novo();
        leitor.Ligar("fulano");
        await Esperar(() => leitor.Estado == EstadoDoTikTok.Conectado);

        _tikTok.Sessao = "nova";
        leitor.Recomecar();

        await Esperar(() => _tikTok.SessoesAbertas.Count == 2);
        Assert.Equal([null, "nova"], _tikTok.SessoesAbertas);
        Assert.Equal("fulano", leitor.Canal);
    }

    [Fact]
    public void Recomecar_Desligado_NaoFazNada()
    {
        using var leitor = Novo();
        leitor.Recomecar();
        Assert.Null(leitor.Canal);
        Assert.Equal(EstadoDoTikTok.Desligado, leitor.Estado);
    }

    /// <summary>As respostas da sala e os WebSockets que o leitor vai receber, na ordem.</summary>
    private sealed class TikTokFalso : IConexaoComOTikTok
    {
        private readonly ConcurrentQueue<object> _salas = new();
        private readonly ConcurrentQueue<SocketFalso> _sockets = new();

        public ConcurrentQueue<string> Salas { get; } = new();

        public List<string> Abertos { get; } = [];

        /// <summary>A próxima resposta da sala: o JSON ou um erro. Sem nenhuma na fila: fora do ar.</summary>
        public void Sala(object resposta) => _salas.Enqueue(resposta);

        public SocketFalso Socket()
        {
            var socket = new SocketFalso();
            _sockets.Enqueue(socket);
            return socket;
        }

        public Task<string> SalaAsync(string canal, CancellationToken cancelar)
        {
            Salas.Enqueue(canal);
            object resposta = _salas.TryDequeue(out object? r) ? r : AmostrasDoTikTok.Sala(aoVivo: false);
            return resposta is Exception e ? Task.FromException<string>(e) : Task.FromResult((string)resposta);
        }

        /// <summary>A lista de presentes em português (a de verdade tem uns 3 MB; aqui, só a Rosa).</summary>
        public string ListaDePresentes { get; set; } = """{"data":{"gifts":[{"id":5655,"name":"Rosa"}]}}""";

        public int PedidosDaLista;

        public Task<string> PresentesAsync(string idDaSala, CancellationToken cancelar)
        {
            Interlocked.Increment(ref PedidosDaLista);
            return Task.FromResult(ListaDePresentes);
        }

        /// <summary>A sessão da conta conectada (null = sem conta) e o que o TikTok diz dela.</summary>
        public string? Sessao { get; set; }

        public SituacaoDaSessao Situacao { get; set; } = SituacaoDaSessao.Valida;

        /// <summary>A sessão usada em cada conexão aberta.</summary>
        public List<string?> SessoesAbertas { get; } = [];

        public bool ComSessao => Sessao != null;

        public Task<SituacaoDaSessao> ConferirSessaoAsync(CancellationToken cancelar) =>
            Task.FromResult(Sessao == null ? SituacaoDaSessao.Valida : Situacao);

        public Task<ISocketDoTikTok> AbrirAsync(string idDaSala, CancellationToken cancelar)
        {
            lock (SessoesAbertas)
                SessoesAbertas.Add(Sessao);
            lock (Abertos)
                Abertos.Add(idDaSala);
            return _sockets.TryDequeue(out SocketFalso? socket)
                ? Task.FromResult<ISocketDoTikTok>(socket)
                : Task.FromException<ISocketDoTikTok>(new WebSocketException("recusado"));
        }
    }

    private sealed class SocketFalso : ISocketDoTikTok
    {
        private readonly Channel<byte[]?> _chegando = Channel.CreateUnbounded<byte[]?>();
        private readonly ConcurrentQueue<string> _tipos = new();

        /// <summary>O tipo de cada quadro que o leitor mandou.</summary>
        public IReadOnlyList<string> Tipos => [.. _tipos];

        public bool Fechado { get; private set; }

        /// <summary>Um quadro do TikTok; null = o TikTok fechou a conexão.</summary>
        public void Chega(byte[]? quadro) => _chegando.Writer.TryWrite(quadro);

        public async Task<byte[]?> ReceberAsync(CancellationToken cancelar) => await _chegando.Reader.ReadAsync(cancelar);

        public Task EnviarAsync(byte[] quadro, CancellationToken cancelar)
        {
            _tipos.Enqueue(Protobuf.Ler(quadro).Texto(7));
            return Task.CompletedTask;
        }

        public void Dispose() => Fechado = true;
    }
}
