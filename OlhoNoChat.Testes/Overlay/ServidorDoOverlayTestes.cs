using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Overlay;

namespace OlhoNoChat.Testes.Overlay;

/// <summary>
/// O servidor do overlay da live: a página e os arquivos dela, o fluxo de eventos (as opções primeiro, depois cada
/// evento na hora, sem nada guardado), a porta ocupada (por outro programa ou por outro Olho no Chat) e o
/// "Encontrar outro endereço".
/// </summary>
public sealed class ServidorDoOverlayTestes : IDisposable
{
    private static readonly HttpClient Navegador = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat.Testes", Guid.NewGuid().ToString("N"));
    private readonly List<ServidorDoOverlay> _servidores = [];

    public ServidorDoOverlayTestes()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "overlay.html"), "<p>overlay</p>");
        File.WriteAllText(Path.Combine(_pasta, "overlay.js"), "// js");
        File.WriteAllText(Path.Combine(_pasta, "overlay.css"), "/* css */");
        File.WriteAllText(Path.Combine(_pasta, "chat.css"), "/* chat */");
        File.WriteAllText(Path.Combine(_pasta, "chat.js"), "// não é do overlay");
    }

    public void Dispose()
    {
        foreach (ServidorDoOverlay servidor in _servidores)
            servidor.Dispose();
        Directory.Delete(_pasta, recursive: true);
    }

    private ServidorDoOverlay Novo(TimeSpan? tentarDeNovo = null)
    {
        var servidor = new ServidorDoOverlay(NullLogger.Instance, _pasta, tentarDeNovo ?? TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200));
        _servidores.Add(servidor);
        return servidor;
    }

    private static int PortaLivre()
    {
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        int porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        ouvinte.Stop();
        return porta;
    }

    private static async Task Esperar(Func<bool> condicao, int vezes = 300)
    {
        for (int i = 0; i < vezes && !condicao(); i++)
            await Task.Delay(10);
        Assert.True(condicao());
    }

    private async Task<(ServidorDoOverlay Servidor, int Porta)> Aberto(string opcoes = """{"maxLines":5}""")
    {
        int porta = PortaLivre();
        ServidorDoOverlay servidor = Novo();
        servidor.Ligar(porta, opcoes);
        await Esperar(() => servidor.Estado == EstadoDoOverlay.Funcionando);
        return (servidor, porta);
    }

    // Um evento do fluxo: o tipo e o JSON
    private sealed record Evento(string Tipo, JsonElement Dados);

    // Lê o fluxo de eventos como o EventSource da página (os comentários e o "retry" ficam de fora)
    private sealed class Fluxo : IDisposable
    {
        private readonly HttpResponseMessage _resposta;
        private readonly StreamReader _leitor;

        private Fluxo(HttpResponseMessage resposta, StreamReader leitor)
        {
            _resposta = resposta;
            _leitor = leitor;
        }

        public static async Task<Fluxo> Abrir(int porta)
        {
            var cliente = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            HttpResponseMessage resposta = await cliente.GetAsync($"http://localhost:{porta}/overlay/eventos", HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal("text/event-stream", resposta.Content.Headers.ContentType?.MediaType);
            return new Fluxo(resposta, new StreamReader(await resposta.Content.ReadAsStreamAsync()));
        }

        // O "version" vem antes do config ao conectar; os testes que não olham para ele pulam
        public bool PularVersao { get; set; } = true;

        public async Task<Evento> Proximo()
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? tipo = null, dados = null;
            while (true)
            {
                string? linha = await _leitor.ReadLineAsync(limite.Token);
                Assert.NotNull(linha);
                if (linha.StartsWith("event: ", StringComparison.Ordinal))
                    tipo = linha["event: ".Length..];
                else if (linha.StartsWith("data: ", StringComparison.Ordinal))
                    dados = linha["data: ".Length..];
                else if (linha.Length == 0 && tipo != null && dados != null)
                {
                    if (tipo == "version" && PularVersao)
                    {
                        tipo = dados = null;
                        continue;
                    }
                    return new Evento(tipo, JsonDocument.Parse(dados).RootElement.Clone());
                }
            }
        }

        public void Dispose()
        {
            _leitor.Dispose();
            _resposta.Dispose();
        }
    }

    [Theory]
    [InlineData("/overlay", "<p>overlay</p>", "text/html")]
    [InlineData("/overlay/", "<p>overlay</p>", "text/html")]
    [InlineData("/overlay.js", "// js", "text/javascript")]
    [InlineData("/overlay.css", "/* css */", "text/css")]
    [InlineData("/chat.css", "/* chat */", "text/css")]
    public async Task Pagina_ServeSoOsArquivosDoOverlay(string caminho, string conteudo, string tipo)
    {
        var (_, porta) = await Aberto();

        HttpResponseMessage resposta = await Navegador.GetAsync($"http://localhost:{porta}{caminho}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(tipo, resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(conteudo, await resposta.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/chat.js")]
    [InlineData("/")]
    [InlineData("/overlay/../chat.js")]
    [InlineData("/Configuracoes.json")]
    public async Task Pagina_OutrosArquivosNaoExistem(string caminho)
    {
        var (_, porta) = await Aberto();

        HttpResponseMessage resposta = await Navegador.GetAsync($"http://localhost:{porta}{caminho}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Quem_DizQueEOOlhoNoChat()
    {
        var (_, porta) = await Aberto();

        string resposta = await Navegador.GetStringAsync($"http://localhost:{porta}/overlay/quem");

        Assert.Contains(ServidorDoOverlay.Marca, resposta);
    }

    [Fact]
    public async Task Eventos_AsOpcoesPrimeiroDepoisCadaEventoNaHora()
    {
        var (servidor, porta) = await Aberto("""{"maxLines":5}""");
        using Fluxo fluxo = await Fluxo.Abrir(porta);
        fluxo.PularVersao = false;

        Evento versao = await fluxo.Proximo();
        Assert.Equal("version", versao.Tipo);
        Assert.Equal(servidor.VersaoDasPaginas(), versao.Dados.GetProperty("v").GetString());
        Evento config = await fluxo.Proximo();
        Assert.Equal("config", config.Tipo);
        Assert.Equal(5, config.Dados.GetProperty("maxLines").GetInt32());
        await Esperar(() => servidor.Conectados == 1);

        servidor.EnviarLinha("""<div class="chat_line">oi</div>""");
        servidor.EnviarRemocao(""".chat_line[data-id="x"]""");
        servidor.EnviarSubstituicao(".chat_line[data-ov=\"1\"]", "<div class=\"chat_line\" data-ov=\"1\">com selo</div>");
        servidor.EnviarTeste("<div class=\"chat_line\">teste</div>");
        servidor.Limpar();

        Evento linha = await fluxo.Proximo();
        Assert.Equal("line", linha.Tipo);
        Assert.Equal("""<div class="chat_line">oi</div>""", linha.Dados.GetProperty("html").GetString());
        Assert.False(linha.Dados.TryGetProperty("test", out _));
        Evento remocao = await fluxo.Proximo();
        Assert.Equal("remove", remocao.Tipo);
        Assert.Equal(""".chat_line[data-id="x"]""", remocao.Dados.GetProperty("selector").GetString());
        Evento troca = await fluxo.Proximo();
        Assert.Equal("replace", troca.Tipo);
        Assert.Equal(".chat_line[data-ov=\"1\"]", troca.Dados.GetProperty("selector").GetString());
        Assert.Equal("<div class=\"chat_line\" data-ov=\"1\">com selo</div>", troca.Dados.GetProperty("html").GetString());
        Evento teste = await fluxo.Proximo();
        Assert.Equal("line", teste.Tipo);
        Assert.True(teste.Dados.GetProperty("test").GetBoolean());
        Assert.Equal("clear", (await fluxo.Proximo()).Tipo);
    }

    [Fact]
    public async Task Eventos_AsOpcoesNovasChegamNaHora()
    {
        var (servidor, porta) = await Aberto("""{"maxLines":5}""");
        using Fluxo fluxo = await Fluxo.Abrir(porta);
        await fluxo.Proximo();
        await Esperar(() => servidor.Conectados == 1);

        servidor.MudarOpcoes("""{"maxLines":9}""");

        Evento config = await fluxo.Proximo();
        Assert.Equal("config", config.Tipo);
        Assert.Equal(9, config.Dados.GetProperty("maxLines").GetInt32());
    }

    [Fact]
    public async Task Eventos_LigarDeNovoNaMesmaPortaSoMandaAsOpcoes()
    {
        var (servidor, porta) = await Aberto("""{"maxLines":5}""");
        using Fluxo fluxo = await Fluxo.Abrir(porta);
        await fluxo.Proximo();
        await Esperar(() => servidor.Conectados == 1);

        servidor.Ligar(porta, """{"maxLines":7}""");

        Evento config = await fluxo.Proximo();
        Assert.Equal(7, config.Dados.GetProperty("maxLines").GetInt32());
        Assert.Equal(1, servidor.Conectados); // não fechou a conexão
    }

    // Só tempo real (decisão do usuário, 2026-11-06): quem conecta não recebe o que veio antes
    [Fact]
    public async Task Conectar_NaoRecebeOQueVeioAntes()
    {
        var (servidor, porta) = await Aberto();
        servidor.EnviarLinha("<div class=\"chat_line\">antes</div>");
        servidor.EnviarRemocao(".x");

        using Fluxo fluxo = await Fluxo.Abrir(porta);
        Assert.Equal("config", (await fluxo.Proximo()).Tipo);
        await Esperar(() => servidor.Conectados == 1);
        servidor.EnviarLinha("<div class=\"chat_line\">depois</div>");

        Evento linha = await fluxo.Proximo();
        Assert.Equal("<div class=\"chat_line\">depois</div>", linha.Dados.GetProperty("html").GetString());
    }

    [Fact]
    public async Task LigarNoutraPorta_FechaAAntiga()
    {
        var (servidor, porta) = await Aberto();
        int outra = PortaLivre();

        servidor.Ligar(outra, "{}");

        await Esperar(() => servidor.Estado == EstadoDoOverlay.Funcionando && servidor.Porta == outra);
        using Fluxo novo = await Fluxo.Abrir(outra);
        Assert.Equal("config", (await novo.Proximo()).Tipo);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Navegador.GetAsync($"http://localhost:{porta}/overlay"));
    }

    [Fact]
    public async Task Desligar_FechaOEnderecoEAsConexoes()
    {
        var (servidor, porta) = await Aberto();
        using Fluxo fluxo = await Fluxo.Abrir(porta);
        await fluxo.Proximo();
        await Esperar(() => servidor.Conectados == 1);

        servidor.Desligar();

        Assert.Equal(EstadoDoOverlay.Desligado, servidor.Estado);
        Assert.Equal(0, servidor.Conectados);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Navegador.GetAsync($"http://localhost:{porta}/overlay"));
    }

    [Fact]
    public async Task PortaOcupadaPorOutroPrograma_AvisaETentaDeNovoSozinho()
    {
        int porta = PortaLivre();
        var outroPrograma = new HttpListener();
        outroPrograma.Prefixes.Add($"http://localhost:{porta}/");
        outroPrograma.Start();
        ServidorDoOverlay servidor = Novo();
        var estados = new List<EstadoDoOverlay>();
        servidor.EstadoMudou += estado =>
        {
            lock (estados)
                estados.Add(estado);
        };

        servidor.Ligar(porta, "{}");
        await Esperar(() => servidor.Estado == EstadoDoOverlay.PortaOcupada);
        await Task.Delay(300); // tentou de novo algumas vezes: o estado não é avisado de novo
        outroPrograma.Close();

        await Esperar(() => servidor.Estado == EstadoDoOverlay.Funcionando);
        lock (estados)
            Assert.Equal([EstadoDoOverlay.PortaOcupada, EstadoDoOverlay.Funcionando], estados);
    }

    [Fact]
    public async Task PortaOcupadaPorOutroOlhoNoChat_DizQueEOutroOlhoNoChat()
    {
        var (_, porta) = await Aberto();
        ServidorDoOverlay segundo = Novo();

        segundo.Ligar(porta, "{}");

        await Esperar(() => segundo.Estado == EstadoDoOverlay.OutroOlhoNoChat);
    }

    [Fact]
    public async Task PortaLivre_SabeSeAPortaAbre()
    {
        var (_, porta) = await Aberto();

        Assert.False(ServidorDoOverlay.PortaLivre(porta));
        Assert.True(ServidorDoOverlay.PortaLivre(PortaLivre()));
    }

    [Fact]
    public void EncontrarPortaLivre_PulaAAtualEAsOcupadas()
    {
        int[] ocupadas = [ServidorDoOverlay.PortasParaTentar[1], ServidorDoOverlay.PortasParaTentar[2]];

        int? nova = ServidorDoOverlay.EncontrarPortaLivre(ServidorDoOverlay.PortasParaTentar[0], porta => !ocupadas.Contains(porta));

        Assert.Equal(ServidorDoOverlay.PortasParaTentar[3], nova);
    }

    [Fact]
    public void EncontrarPortaLivre_NenhumaLivre()
    {
        Assert.Null(ServidorDoOverlay.EncontrarPortaLivre(ServidorDoOverlay.PortaPadrao, _ => false));
    }

    [Fact]
    public void EncontrarPortaLivre_DeUmaPortaDigitadaVoltaParaAPadrao()
    {
        Assert.Equal(ServidorDoOverlay.PortaPadrao, ServidorDoOverlay.EncontrarPortaLivre(12345, _ => true));
    }

    [Fact]
    public void Portas_AsDeTentarFicamForaDasTemporariasDoWindowsELongeDasDaTwitch()
    {
        Assert.All(ServidorDoOverlay.PortasParaTentar, porta =>
        {
            Assert.True(ServidorDoOverlay.PortaValida(porta));
            Assert.True(porta < 49152);
            Assert.DoesNotContain(porta, OlhoNoChat.Twitch.AutorizacaoNoNavegador.Portas);
        });
        Assert.Equal(ServidorDoOverlay.PortasParaTentar.Count, ServidorDoOverlay.PortasParaTentar.Distinct().Count());
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(8987, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    [InlineData(0, false)]
    public void PortaValida_De1024A65535(int porta, bool valida)
    {
        Assert.Equal(valida, ServidorDoOverlay.PortaValida(porta));
    }

    [Fact]
    public void Endereco_SempreLocalhost()
    {
        Assert.Equal("http://localhost:8987/overlay", ServidorDoOverlay.EnderecoDa(8987));
    }
}
