using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Kick;
using static OlhoNoChat.Testes.Kick.AmostrasDaKick;

namespace OlhoNoChat.Testes.Kick;

/// <summary>
/// O leitor da Kick com uma Kick falsa: o que manda no WebSocket (connect e subscribe, pong só depois de ping, troca do
/// token com o mesmo cliente), quando conecta de novo (conexão que caiu, ficou muda ou foi recusada) e quando para
/// (canal que não existe).
/// </summary>
public class LeitorDaKickTestes
{
    private static readonly TimeSpan Espera = TimeSpan.FromMilliseconds(50);
    private readonly KickFalsa _kick = new();
    private readonly ConcurrentQueue<EstadoDaKick> _estados = new();
    private readonly ConcurrentQueue<EventoDaKick> _eventos = new();

    private LeitorDaKick Novo(TimeSpan? tempoSemNada = null, TimeSpan? renovarACada = null)
    {
        var leitor = new LeitorDaKick(NullLogger.Instance, _kick, Espera, tempoSemNada ?? TimeSpan.FromSeconds(10), renovarACada);
        leitor.EstadoMudou += estado => _estados.Enqueue(estado);
        leitor.EventoRecebido += evento => _eventos.Enqueue(evento);
        return leitor;
    }

    private static async Task Esperar(Func<bool> condicao, int vezes = 300)
    {
        for (int i = 0; i < vezes && !condicao(); i++)
            await Task.Delay(10);
        Assert.True(condicao());
    }

    // Um socket que responde ao connect e ao subscribe
    private SocketFalso SocketQueAceita()
    {
        SocketFalso socket = _kick.Socket();
        socket.Chega(Conectou + "\n" + Assinou);
        return socket;
    }

    [Fact]
    public async Task Ligar_ConectaEAssinaASalaDoCanal()
    {
        SocketFalso socket = SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar("seucanal");

        Assert.Equal(EstadoDaKick.Conectando, leitor.Estado);
        await Esperar(() => leitor.Estado == EstadoDaKick.Conectado);
        Assert.Equal(["seucanal"], _kick.Canais);
        Assert.Equal(["wss://realtime.us-east-1.platform.kick.com/connection/websocket"], _kick.Abertos);
        Assert.Equal(QuadrosDaKick.ConectarEAssinar("token-1", IdDaSala), socket.Enviados.First());
        // O endereço e o token pedidos com o mesmo cliente
        Assert.Equal(_kick.ClientesDoToken.Single(), _kick.ClientesDoEndereco.Single());
    }

    [Fact]
    public async Task Mensagens_ChegamComoEventos()
    {
        SocketFalso socket = SocketQueAceita();
        socket.Chega(Mensagem("m1", "Viewer1", "oi") + "\n" + Mensagem("m2", "Viewer2", "olá"));
        using var leitor = Novo();
        leitor.Ligar("seucanal");

        await Esperar(() => _eventos.Count == 2);
        Assert.Equal(["m1", "m2"], _eventos.Select(e => e.Mensagem!.Id));
    }

    [Fact]
    public async Task Ping_RespondeComPong_ENuncaSemPing()
    {
        SocketFalso socket = SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar("seucanal");
        await Esperar(() => leitor.Estado == EstadoDaKick.Conectado);
        await Task.Delay(Espera);
        Assert.DoesNotContain(QuadrosDaKick.Pong, socket.Enviados);

        socket.Chega("{}");
        await Esperar(() => socket.Enviados.Count(q => q == QuadrosDaKick.Pong) == 1);
    }

    [Fact]
    public async Task Token_TrocadoComOMesmoCliente()
    {
        SocketFalso socket = SocketQueAceita();
        using var leitor = Novo(renovarACada: TimeSpan.FromMilliseconds(30));
        leitor.Ligar("seucanal");

        await Esperar(() => socket.Enviados.Count(q => q.Contains("\"refresh\"")) >= 2);
        Assert.Contains("""{"id":3,"refresh":{"token":"token-2"}}""", socket.Enviados);
        Assert.Single(_kick.ClientesDoToken.Distinct());
    }

    [Fact]
    public async Task CanalQueNaoExiste_NaoProcuraDeNovo_AteLigarOutraVez()
    {
        _kick.CanalNaoExiste = true;
        using var leitor = Novo();
        leitor.Ligar("naoexiste");

        await Esperar(() => leitor.Estado == EstadoDaKick.CanalNaoExiste);
        await Task.Delay(Espera * 4);
        Assert.Single(_kick.Canais);

        leitor.Ligar("naoexiste"); // confirmar de novo na faixa procura de novo
        await Esperar(() => _kick.Canais.Count == 2);
    }

    [Theory]
    [InlineData("korekore-ch", "korekore_ch")] // salvo com "-" pela versão antiga; o endereço tem "_"
    [InlineData("patron_efm", "patron-efm")]   // digitado com "_"; o endereço tem "-"
    public async Task NaoAchou_TentaComOOutroSinal(string salvo, string endereco)
    {
        _kick.Existentes = [endereco];
        SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar(salvo);

        await Esperar(() => leitor.Estado == EstadoDaKick.Conectado);
        Assert.Equal([salvo, endereco], _kick.Canais);
    }

    [Fact]
    public async Task NaoAchouNenhumaForma_CanalNaoExiste()
    {
        _kick.Existentes = [];
        using var leitor = Novo();
        leitor.Ligar("fulano_1");

        await Esperar(() => leitor.Estado == EstadoDaKick.CanalNaoExiste);
        Assert.Equal(["fulano_1", "fulano-1"], _kick.Canais);
    }

    [Fact]
    public async Task ConexaoCaiu_ConectaDeNovoSemFicarVermelho()
    {
        SocketFalso primeiro = SocketQueAceita();
        primeiro.Chega(null); // a Kick fechou
        SocketFalso segundo = SocketQueAceita();
        segundo.Chega(Mensagem("m1", "Viewer1", "voltei"));
        using var leitor = Novo();
        leitor.Ligar("seucanal");

        await Esperar(() => _eventos.Count == 1);
        Assert.Equal(2, _kick.Abertos.Count);
        Assert.DoesNotContain(EstadoDaKick.SemConexao, _estados);
        Assert.Equal([EstadoDaKick.Conectando, EstadoDaKick.Conectado, EstadoDaKick.Conectando, EstadoDaKick.Conectado], _estados.Take(4));
        Assert.True(primeiro.Fechado);
    }

    [Fact]
    public async Task PedidoParaDesconectar_ConectaDeNovo()
    {
        SocketFalso primeiro = SocketQueAceita();
        primeiro.Chega("""{"push":{"disconnect":{"code":3005,"reason":"stale"}}}""");
        SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar("seucanal");

        await Esperar(() => _kick.Abertos.Count == 2);
        Assert.True(primeiro.Fechado);
    }

    [Fact]
    public async Task SemNadaDaKick_DerrubaEConectaDeNovo()
    {
        SocketFalso mudo = SocketQueAceita();
        SocketQueAceita();
        using var leitor = Novo(tempoSemNada: TimeSpan.FromMilliseconds(100));
        leitor.Ligar("seucanal");

        await Esperar(() => _kick.Abertos.Count == 2);
        Assert.True(mudo.Fechado);
    }

    [Fact]
    public async Task ConnectRecusado_SemConexaoETentaDeNovo()
    {
        SocketFalso recusado = _kick.Socket();
        recusado.Chega("""{"id":1,"error":{"code":109,"message":"token expired"}}""");
        using var leitor = Novo();
        leitor.Ligar("seucanal");

        await Esperar(() => _estados.Contains(EstadoDaKick.SemConexao));
        // Pelo menos 2: o leitor continua tentando a cada 50 ms, e numa máquina lenta a conferência pode chegar depois
        await Esperar(() => _kick.Canais.Count >= 2);
        Assert.True(recusado.Fechado);
    }

    [Fact]
    public async Task KickNaoRespondeu_SemConexaoETentaDeNovo()
    {
        _kick.ErroNoCanal = new HttpRequestException("sem internet");
        using var leitor = Novo();
        leitor.Ligar("seucanal");

        await Esperar(() => _estados.Contains(EstadoDaKick.SemConexao));
        // Pelo menos 2: o leitor continua tentando a cada 50 ms, e numa máquina lenta a conferência pode chegar depois
        await Esperar(() => _kick.Canais.Count >= 2);
    }

    [Fact]
    public async Task WebSocketRecusado_SemConexao()
    {
        using var leitor = Novo(); // sem socket na fila: a falsa recusa
        leitor.Ligar("seucanal");

        await Esperar(() => _estados.Contains(EstadoDaKick.SemConexao));
    }

    [Fact]
    public async Task MesmoCanal_NaoRecomeca()
    {
        SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar("seucanal");
        leitor.Ligar("seucanal");
        await Task.Delay(Espera);

        Assert.Single(_kick.Canais);
    }

    [Fact]
    public async Task OutroCanal_CancelaOAnterior_EIgnoraOQueEleManda()
    {
        SocketFalso antigo = SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar("seucanal");
        await Esperar(() => leitor.Estado == EstadoDaKick.Conectado);

        leitor.Ligar("outro");
        antigo.Chega(Mensagem("m1", "Viewer1", "do antigo"));
        // Pelo menos 2: sem socket para o "outro", o leitor continua tentando a cada 50 ms (numa máquina lenta a
        // conferência pode chegar depois da 2ª tentativa)
        await Esperar(() => _kick.Canais.Count >= 2);
        Assert.Equal("outro", _kick.Canais.ElementAt(1));
        await Task.Delay(Espera);

        Assert.Empty(_eventos);
        Assert.Equal("outro", leitor.Canal);
    }

    [Fact]
    public async Task Desligar_ParaTudo()
    {
        SocketFalso socket = SocketQueAceita();
        using var leitor = Novo();
        leitor.Ligar("seucanal");
        await Esperar(() => leitor.Estado == EstadoDaKick.Conectado);

        leitor.Desligar();

        Assert.Equal(EstadoDaKick.Desligado, leitor.Estado);
        Assert.Null(leitor.Canal);
        await Esperar(() => socket.Fechado);
    }

    /// <summary>O canal, os tokens (token-1, token-2...) e os WebSockets que o leitor vai receber, na ordem.</summary>
    private sealed class KickFalsa : IConexaoComAKick
    {
        private readonly ConcurrentQueue<SocketFalso> _sockets = new();
        private int _tokens;

        public bool CanalNaoExiste { get; set; }

        /// <summary>Só estes nomes existem na Kick (null = qualquer um).</summary>
        public string[]? Existentes { get; set; }

        /// <summary>O canal dá este erro uma vez.</summary>
        public Exception? ErroNoCanal { get; set; }

        public ConcurrentQueue<string> Canais { get; } = new();

        public ConcurrentQueue<string> Abertos { get; } = new();

        public ConcurrentQueue<string> ClientesDoToken { get; } = new();

        public ConcurrentQueue<string> ClientesDoEndereco { get; } = new();

        public SocketFalso Socket()
        {
            var socket = new SocketFalso();
            _sockets.Enqueue(socket);
            return socket;
        }

        public Task<string?> CanalAsync(string canal, CancellationToken cancelar)
        {
            Canais.Enqueue(canal);
            if (ErroNoCanal is { } erro)
            {
                ErroNoCanal = null;
                return Task.FromException<string?>(erro);
            }
            return Task.FromResult(CanalNaoExiste || (Existentes != null && !Existentes.Contains(canal)) ? null : (string?)AmostrasDaKick.Canal);
        }

        public Task<string> TokenAsync(string idDoCliente, CancellationToken cancelar)
        {
            ClientesDoToken.Enqueue(idDoCliente);
            return Task.FromResult("token-" + Interlocked.Increment(ref _tokens));
        }

        public Task<string> EnderecoAsync(long idDoCanal, string idDoCliente, CancellationToken cancelar)
        {
            Assert.Equal(IdDoCanal, idDoCanal);
            ClientesDoEndereco.Enqueue(idDoCliente);
            return Task.FromResult(ConexaoComAKick.LerEndereco(AmostrasDaKick.Endereco));
        }

        public Task<ISocketDaKick> AbrirAsync(string endereco, CancellationToken cancelar)
        {
            Abertos.Enqueue(endereco);
            return _sockets.TryDequeue(out SocketFalso? socket)
                ? Task.FromResult<ISocketDaKick>(socket)
                : Task.FromException<ISocketDaKick>(new WebSocketException("recusado"));
        }
    }

    private sealed class SocketFalso : ISocketDaKick
    {
        private readonly Channel<string?> _chegando = Channel.CreateUnbounded<string?>();
        private readonly ConcurrentQueue<string> _enviados = new();

        /// <summary>Os quadros que o leitor mandou.</summary>
        public IReadOnlyList<string> Enviados => [.. _enviados];

        public bool Fechado { get; private set; }

        public string Fechamento => "3005 teste";

        /// <summary>Um quadro da Kick; null = a Kick fechou a conexão.</summary>
        public void Chega(string? quadro) => _chegando.Writer.TryWrite(quadro);

        public async Task<string?> ReceberAsync(CancellationToken cancelar) => await _chegando.Reader.ReadAsync(cancelar);

        public Task EnviarAsync(string quadro, CancellationToken cancelar)
        {
            _enviados.Enqueue(quadro);
            return Task.CompletedTask;
        }

        public void Dispose() => Fechado = true;
    }
}
