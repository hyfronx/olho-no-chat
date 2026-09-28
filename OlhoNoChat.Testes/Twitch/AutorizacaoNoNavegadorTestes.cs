using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public class AutorizacaoNoNavegadorTestes
{
    private static readonly HttpClient Navegador = new() { Timeout = TimeSpan.FromSeconds(5) };

    [Fact]
    public void Endereco_PedeAsTresPermissoesESempreMostraAPagina()
    {
        string endereco = AutorizacaoNoNavegador.EnderecoDeAutorizacao(8981, "abc123");

        Assert.StartsWith("https://id.twitch.tv/oauth2/authorize?", endereco);
        var partes = HttpUtility.ParseQueryString(new Uri(endereco).Query);
        Assert.Equal("token", partes["response_type"]);
        Assert.Equal("zrqsilh31pbdlfjb81onhulkvzytgh", partes["client_id"]);
        Assert.Equal("http://localhost:8981/auth", partes["redirect_uri"]);
        Assert.Equal("user:write:chat channel:read:redemptions user:read:emotes", partes["scope"]);
        Assert.Equal("true", partes["force_verify"]);
        Assert.Equal("abc123", partes["state"]);
    }

    [Fact]
    public void Portas_AsQuatroRegistradasNaTwitchNaOrdem()
    {
        Assert.Equal([8981, 28981, 38981, 45981], AutorizacaoNoNavegador.Portas);
    }

    [Theory]
    [InlineData("?access_token=tok&scope=x&state=s1&token_type=bearer", "s1", true, "tok")]
    [InlineData("?error=access_denied&error_description=The+user+denied&state=s1", "s1", true, null)]
    [InlineData("?access_token=tok&state=outro", "s1", false, null)]
    [InlineData("?access_token=tok", "s1", false, null)]
    [InlineData("", "s1", false, null)]
    public void LerRetorno_SoValeComOStateCerto(string query, string state, bool desteConectar, string? token)
    {
        Assert.Equal((desteConectar, token), AutorizacaoNoNavegador.LerRetorno(query, state));
    }

    private static int PortaLivre()
    {
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        int porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        ouvinte.Stop();
        return porta;
    }

    // O "navegador" dos testes recebe o endereço de autorização e responde como a Twitch faria
    private static AutorizacaoNoNavegador Autorizacao(Func<int, string, Task> navegador, IReadOnlyList<int> portas, TimeSpan? limite = null) =>
        new(NullLogger<AutorizacaoNoNavegador>.Instance,
            endereco =>
            {
                var partes = HttpUtility.ParseQueryString(new Uri(endereco).Query);
                int porta = new Uri(partes["redirect_uri"]!).Port;
                _ = Task.Run(() => navegador(porta, partes["state"]!));
            },
            portas, limite ?? TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Conectar_RecebeOTokenPelaPaginaDeResposta()
    {
        string pagina = "";
        HttpStatusCode outroCaminho = 0;
        var autorizacao = Autorizacao(async (porta, state) =>
        {
            pagina = await Navegador.GetStringAsync($"http://localhost:{porta}/auth");
            outroCaminho = (await Navegador.GetAsync($"http://localhost:{porta}/favicon.ico")).StatusCode;
            await Navegador.GetAsync($"http://localhost:{porta}/callback?state=errado&access_token=falso");
            await Navegador.GetAsync($"http://localhost:{porta}/callback?access_token=certo&state={state}&token_type=bearer");
        }, [PortaLivre()]);

        var resultado = await autorizacao.ConectarAsync();

        Assert.Equal(new AutorizacaoNoNavegador.Resultado(AutorizacaoNoNavegador.Fim.Token, "certo"), resultado);
        Assert.Contains("Conta conectada!", pagina);
        Assert.Contains("Conexão cancelada", pagina);
        Assert.Contains("/callback?", pagina);
        Assert.Equal(HttpStatusCode.NotFound, outroCaminho);
        Assert.False(autorizacao.EstaEsperando);
    }

    [Fact]
    public async Task Conectar_RecusadoTerminaSemToken()
    {
        var autorizacao = Autorizacao((porta, state) =>
            Navegador.GetAsync($"http://localhost:{porta}/callback?error=access_denied&state={state}"), [PortaLivre()]);

        Assert.Equal(AutorizacaoNoNavegador.Fim.SemToken, (await autorizacao.ConectarAsync()).Fim);
    }

    [Fact]
    public async Task Conectar_UsaAProximaPortaLivreEFechaOServidorNoFim()
    {
        int ocupada = PortaLivre();
        using var outroPrograma = new HttpListener();
        outroPrograma.Prefixes.Add($"http://localhost:{ocupada}/");
        outroPrograma.Start();
        int livre = PortaLivre();
        int portaUsada = 0;

        var autorizacao = Autorizacao((porta, state) =>
        {
            portaUsada = porta;
            return Navegador.GetAsync($"http://localhost:{porta}/callback?access_token=t&state={state}");
        }, [ocupada, livre]);

        Assert.Equal(AutorizacaoNoNavegador.Fim.Token, (await autorizacao.ConectarAsync()).Fim);
        Assert.Equal(livre, portaUsada);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Navegador.GetAsync($"http://localhost:{livre}/auth"));
    }

    [Fact]
    public async Task Conectar_TodasAsPortasOcupadasNaoAbreONavegador()
    {
        int ocupada = PortaLivre();
        using var outroPrograma = new HttpListener();
        outroPrograma.Prefixes.Add($"http://localhost:{ocupada}/");
        outroPrograma.Start();
        bool abriu = false;
        var autorizacao = new AutorizacaoNoNavegador(NullLogger<AutorizacaoNoNavegador>.Instance, _ => abriu = true,
            [ocupada], TimeSpan.FromSeconds(10));

        Assert.Equal(AutorizacaoNoNavegador.Fim.PortasOcupadas, (await autorizacao.ConectarAsync()).Fim);
        Assert.False(abriu);
    }

    [Fact]
    public async Task Conectar_CancelarEOLimiteTerminamSemToken()
    {
        var autorizacao = Autorizacao((_, _) => Task.CompletedTask, [PortaLivre()]);
        var esperando = autorizacao.ConectarAsync();
        Assert.True(autorizacao.EstaEsperando);
        Assert.Equal(AutorizacaoNoNavegador.Fim.JaEmAndamento, (await autorizacao.ConectarAsync()).Fim);

        autorizacao.Cancelar();
        Assert.Equal(AutorizacaoNoNavegador.Fim.SemToken, (await esperando).Fim);
        Assert.False(autorizacao.EstaEsperando);

        var curta = Autorizacao((_, _) => Task.CompletedTask, [PortaLivre()], TimeSpan.FromMilliseconds(300));
        Assert.Equal(AutorizacaoNoNavegador.Fim.SemToken, (await curta.ConectarAsync()).Fim);
    }
}
