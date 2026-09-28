using System.Net;

namespace OlhoNoChat.Testes.Twitch;

public class ContaDaTwitchTestes
{
    private static TwitchFalsa TwitchQueConfirma() => new TwitchFalsa()
        .Quando("GET", "oauth2/validate", HttpStatusCode.OK, Montar.ValidacaoOk)
        .Quando("GET", "helix/users", HttpStatusCode.OK, Montar.PerfilOk)
        .Quando("POST", "oauth2/revoke", HttpStatusCode.OK);

    [Fact]
    public async Task Verificar_SemTokenNaoChamaATwitch()
    {
        var twitch = TwitchQueConfirma();
        var conta = Montar.Conta(twitch, new ContaSalvaNaMemoria());

        Assert.False(await conta.VerificarAsync());
        Assert.Empty(twitch.Pedidos);
    }

    [Fact]
    public async Task Verificar_GuardaIdLoginNomeFotoEPermissoes()
    {
        var twitch = TwitchQueConfirma();
        var salva = new ContaSalvaNaMemoria { Token = "tok" };
        var conta = Montar.Conta(twitch, salva);
        Assert.True(conta.PodeEnviar);      // suposto antes da verificação
        Assert.False(conta.PodeLerEmotes);

        Assert.True(await conta.VerificarAsync());

        Assert.Equal(("121292674", "hyfronx", "Hyfronx"), (salva.Id, salva.Login, salva.NomeDeExibicao));
        Assert.Equal("https://exemplo/foto.png", conta.Foto);
        Assert.True(conta.EstaConectada);
        Assert.True(conta.PodeEnviar && conta.PodeLerEmotes && conta.PermissoesConferidas);
        Assert.Equal("Hyfronx", conta.NomeMostrado);

        var validacao = twitch.PedidosPara("validate").Single();
        Assert.Equal("OAuth tok", validacao.Autorizacao);
        var perfil = twitch.PedidosPara("helix/users").Single();
        Assert.Equal("Bearer tok", perfil.Autorizacao);
        Assert.Equal("zrqsilh31pbdlfjb81onhulkvzytgh", perfil.ClientId);
    }

    [Fact]
    public async Task Verificar_SoGravaEAvisaQuandoAlgoMudou()
    {
        var twitch = TwitchQueConfirma();
        var salva = new ContaSalvaNaMemoria { Token = "tok" };
        var conta = Montar.Conta(twitch, salva);
        int avisos = 0;
        conta.Mudou += () => avisos++;

        await conta.VerificarAsync();
        Assert.Equal((1, 1), (salva.Gravacoes, avisos));

        await conta.VerificarAsync(); // nada mudou (acontece a cada Configurações aberta)
        Assert.Equal((1, 1), (salva.Gravacoes, avisos));
    }

    [Fact]
    public async Task Verificar_401EsqueceAConta()
    {
        var twitch = new TwitchFalsa().Quando("GET", "validate", HttpStatusCode.Unauthorized, """{"status":401,"message":"invalid access token"}""");
        var salva = Montar.ContaConectada();
        var conta = Montar.Conta(twitch, salva);
        bool avisou = false;
        conta.Mudou += () => avisou = true;

        Assert.False(await conta.VerificarAsync());

        Assert.Equal(("", "", "", ""), (salva.Token, salva.Id, salva.Login, salva.NomeDeExibicao));
        Assert.False(conta.EstaConectada);
        Assert.True(avisou);
        Assert.Equal(1, salva.Gravacoes);
        Assert.Empty(twitch.PedidosPara("revoke")); // expirado: não revoga
    }

    [Fact]
    public async Task Verificar_AcessoDeOutroAplicativoEEsquecido()
    {
        var twitch = new TwitchFalsa().Quando("GET", "validate", HttpStatusCode.OK,
            """{"client_id":"outro","login":"hyfronx","scopes":[],"user_id":"1"}""");
        var salva = Montar.ContaConectada();

        Assert.False(await Montar.Conta(twitch, salva).VerificarAsync());
        Assert.Equal("", salva.Token);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Verificar_OutroErroMantemAConta(HttpStatusCode codigo)
    {
        var twitch = new TwitchFalsa().Quando("GET", "validate", codigo);
        var salva = Montar.ContaConectada();

        Assert.True(await Montar.Conta(twitch, salva).VerificarAsync());
        Assert.Equal("tok", salva.Token);
        Assert.Equal(0, salva.Gravacoes);
    }

    [Fact]
    public async Task Verificar_SemInternetMantemAConta()
    {
        var salva = Montar.ContaConectada();
        Assert.True(await Montar.Conta(new TwitchFalsa(), salva).VerificarAsync());
        Assert.Equal("tok", salva.Token);
    }

    [Fact]
    public async Task Verificar_AcessoDeAntesDa1018NaoLeEmotes()
    {
        var twitch = TwitchQueConfirma().Quando("GET", "validate", HttpStatusCode.OK,
            """{"client_id":"zrqsilh31pbdlfjb81onhulkvzytgh","login":"hyfronx","scopes":["channel:read:redemptions","user:write:chat"],"user_id":"121292674"}""");
        var conta = Montar.Conta(twitch, new ContaSalvaNaMemoria { Token = "tok" });

        await conta.VerificarAsync();
        Assert.True(conta.PodeEnviar);
        Assert.False(conta.PodeLerEmotes);
        Assert.True(conta.PermissoesConferidas);
    }

    [Fact]
    public async Task Verificar_FalhaNoPerfilNaoImporta()
    {
        var twitch = new TwitchFalsa().Quando("GET", "validate", HttpStatusCode.OK, Montar.ValidacaoOk)
                                      .Quando("GET", "helix/users", HttpStatusCode.InternalServerError);
        var salva = new ContaSalvaNaMemoria { Token = "tok" };
        var conta = Montar.Conta(twitch, salva);

        Assert.True(await conta.VerificarAsync());
        Assert.Equal("hyfronx", conta.NomeMostrado); // sem nome de exibição, o login
        Assert.Equal("", conta.Foto);
    }

    [Fact]
    public async Task VerificarUmaVez_ReaproveitaOResultadoDaExecucao()
    {
        var twitch = TwitchQueConfirma();
        var conta = Montar.Conta(twitch, new ContaSalvaNaMemoria { Token = "tok" });

        Assert.True(await conta.VerificarUmaVezAsync());
        Assert.True(await conta.VerificarUmaVezAsync());
        Assert.Single(twitch.PedidosPara("validate"));
    }

    [Fact]
    public async Task Conectar_TokenConfirmadoFicaERevogaOAntigo()
    {
        var twitch = TwitchQueConfirma();
        var salva = Montar.ContaConectada(); // "tok"
        var conta = Montar.Conta(twitch, salva);

        Assert.True(await conta.ConectarAsync("novo"));
        Assert.Equal("novo", salva.Token);
        Assert.True(salva.Gravacoes >= 1);

        await EsperarAsync(() => twitch.PedidosPara("revoke").Any());
        var revogacao = twitch.PedidosPara("revoke").Single();
        Assert.Contains("token=tok", revogacao.Corpo);
        Assert.Contains("client_id=zrqsilh31pbdlfjb81onhulkvzytgh", revogacao.Corpo);
    }

    [Fact]
    public async Task Conectar_PrimeiraConexaoSemInternetEsqueceOToken()
    {
        var salva = new ContaSalvaNaMemoria();
        var conta = Montar.Conta(new TwitchFalsa(), salva);

        Assert.False(await conta.ConectarAsync("novo"));
        Assert.Equal("", salva.Token);
    }

    [Fact]
    public async Task Conectar_ReconexaoSemInternetContaComoConectada()
    {
        // Já havia id salvo: um erro de rede não desconecta
        var salva = Montar.ContaConectada();
        Assert.True(await Montar.Conta(new TwitchFalsa(), salva).ConectarAsync("novo"));
        Assert.Equal("novo", salva.Token);
    }

    [Fact]
    public async Task Desconectar_EsqueceNaHoraERevogaEmSegundoPlano()
    {
        var twitch = TwitchQueConfirma();
        var salva = Montar.ContaConectada();
        var conta = Montar.Conta(twitch, salva);

        conta.Desconectar();
        Assert.False(conta.EstaConectada);
        Assert.Equal(1, salva.Gravacoes);

        await EsperarAsync(() => twitch.PedidosPara("revoke").Any());
        Assert.Contains("token=tok", twitch.PedidosPara("revoke").Single().Corpo);
    }

    [Fact]
    public async Task CanalExiste_ListaVaziaQuerDizerQueNao()
    {
        var twitch = new TwitchFalsa()
            .Quando("GET", "users?login=existe", HttpStatusCode.OK, """{"data":[{"id":"42","login":"existe"}]}""")
            .Quando("GET", "users?login=naoexiste", HttpStatusCode.OK, """{"data":[]}""");
        var conta = Montar.Conta(twitch, Montar.ContaConectada());

        Assert.True(await conta.CanalExisteAsync("existe"));
        Assert.False(await conta.CanalExisteAsync("naoexiste"));
        Assert.Null(await conta.CanalExisteAsync("semrede")); // sem rota = sem internet
        Assert.Null(await Montar.Conta(twitch, new ContaSalvaNaMemoria()).CanalExisteAsync("existe")); // sem conta
    }

    internal static async Task EsperarAsync(Func<bool> condicao, int segundos = 2)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (!condicao() && DateTime.UtcNow < limite)
            await Task.Delay(20);
        Assert.True(condicao());
    }
}
