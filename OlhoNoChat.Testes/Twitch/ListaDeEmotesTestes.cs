using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;
using E = OlhoNoChat.Twitch.ListaDeEmotes.EmoteDaTwitch;

namespace OlhoNoChat.Testes.Twitch;

public class ListaDeEmotesTestes
{
    private static readonly Dictionary<string, string> SemNomes = [];

    [Fact]
    public void Agrupar_EsteCanalPrimeiroOsOutrosPeloNomeEOsGlobaisNoFim()
    {
        var emotes = new[]
        {
            new E("1", "zetaHi", "30"), new E("2", "Kappa", "0"), new E("3", "meuHype", "99"),
            new E("4", "alfaLol", "20"), new E("5", "LUL", ""), new E("6", "twHi", "twitch"),
        };
        var nomes = new Dictionary<string, string> { ["30"] = "Zeta", ["20"] = "alfa", ["99"] = "MeuCanal" };

        var grupos = ListaDeEmotes.Agrupar(emotes, "99", "meucanal", nomes);

        Assert.Equal(["MeuCanal (este canal)", "alfa", "Zeta", "Globais da Twitch"], grupos.Select(g => g.Titulo));
        Assert.Equal(["Kappa", "LUL", "twHi"], grupos[^1].Emotes.Select(e => e.Nome));
    }

    [Fact]
    public void Agrupar_SoNomesQueSaoUmaPalavraSemSmileysNemRepetidos()
    {
        var emotes = new[]
        {
            new E("1", ":)", "0"), new E("2", "<3", "0"), new E("3", "R-)", "0"), new E("4", "O_o", "0"), new E("5", "o_O", "0"),
            new E("6", "Kappa", "0"), new E("7", "Kappa", "1"), new E("", "SemId", "0"), new E("8", "Com_Sublinhado1", "0"),
        };

        var grupos = ListaDeEmotes.Agrupar(emotes, null, "", SemNomes);

        var grupo = Assert.Single(grupos);
        Assert.Equal(["Kappa", "Com_Sublinhado1"], grupo.Emotes.Select(e => e.Nome));
        Assert.Equal("6", grupo.Emotes[0].Id); // repetido: fica o primeiro
    }

    [Fact]
    public void Agrupar_SemNomeOCanalDoChatUsaOLoginEOsOutrosOutroCanal()
    {
        var grupos = ListaDeEmotes.Agrupar([new E("1", "a1", "99"), new E("2", "b1", "50")], "99", "meucanal", SemNomes);
        Assert.Equal(["meucanal (este canal)", "Outro canal"], grupos.Select(g => g.Titulo));
    }

    private static (ListaDeEmotes Lista, TwitchFalsa Twitch, Action<TimeSpan> Avancar) Criar(
        TwitchFalsa twitch, bool podeLerEmotes = true)
    {
        var agora = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        twitch.Quando("GET", "validate", HttpStatusCode.OK, podeLerEmotes
            ? Montar.ValidacaoOk
            : """{"client_id":"zrqsilh31pbdlfjb81onhulkvzytgh","login":"hyfronx","scopes":["user:write:chat"],"user_id":"121292674"}""");
        var api = new ApiDaTwitch(twitch);
        var conta = new ContaDaTwitch(api, Montar.ContaConectada(), NullLogger<ContaDaTwitch>.Instance);
        conta.VerificarAsync().GetAwaiter().GetResult();
        DateTime atual = agora;
        var lista = new ListaDeEmotes(api, conta, NullLogger<ListaDeEmotes>.Instance, () => atual);
        return (lista, twitch, tempo => atual += tempo);
    }

    private static TwitchFalsa TwitchComEmotes() => new TwitchFalsa()
        .Quando("GET", "users?login=meucanal", HttpStatusCode.OK, """{"data":[{"id":"99"}]}""")
        .Quando("GET", "users?id=", HttpStatusCode.OK, """{"data":[{"id":"99","display_name":"MeuCanal"},{"id":"50","display_name":"Outro"}]}""")
        .Quando("GET", "emotes/user", HttpStatusCode.OK,
            """{"data":[{"id":"1","name":"meuHype","emote_type":"subscriptions","owner_id":"99"},{"id":"2","name":":)","emote_type":"smilies","owner_id":"0"}],"pagination":{"cursor":"PAG2"}}""")
        .Quando("GET", "after=PAG2", HttpStatusCode.OK,
            """{"data":[{"id":"3","name":"outroLol","emote_type":"follower","owner_id":"50"},{"id":"4","name":"Kappa","emote_type":"globals","owner_id":"0"}],"pagination":{}}""")
        .Quando("GET", "emotes/global", HttpStatusCode.OK,
            """{"data":[{"id":"n1","name":"NovoGlobal"},{"id":"k","name":"Kappa"}]}""");

    [Fact]
    public async Task Buscar_SegueAsPaginasETiraOsSmilies()
    {
        var (lista, twitch, _) = Criar(TwitchComEmotes());

        var resultado = await lista.BuscarAsync("meucanal");

        Assert.False(resultado.SoGlobais);
        Assert.Equal(["MeuCanal (este canal)", "Outro", "Globais da Twitch"], resultado.Grupos.Select(g => g.Titulo));
        Assert.DoesNotContain(resultado.Grupos.SelectMany(g => g.Emotes), e => e.Nome == ":)");
        var primeira = twitch.PedidosPara("emotes/user").First();
        Assert.Contains("user_id=121292674", primeira.Endereco);
        Assert.Contains("broadcaster_id=99", primeira.Endereco);
        Assert.Equal(2, twitch.PedidosPara("emotes/user").Count());
    }

    [Fact]
    public async Task Buscar_SemCanalNaoMandaBroadcaster()
    {
        var (lista, twitch, _) = Criar(TwitchComEmotes());
        await lista.BuscarAsync("");
        Assert.DoesNotContain("broadcaster_id", twitch.PedidosPara("emotes/user").First().Endereco);
    }

    [Fact]
    public async Task Buscar_NoMaximo30Paginas()
    {
        var twitch = TwitchComEmotes().Quando("GET", "emotes/user", HttpStatusCode.OK,
            """{"data":[{"id":"1","name":"a1","owner_id":"99"}],"pagination":{"cursor":"SEMPRE"}}""");
        var (lista, _, _) = Criar(twitch);

        await lista.BuscarAsync("meucanal");
        Assert.Equal(30, twitch.PedidosPara("emotes/user").Count());
    }

    [Fact]
    public async Task Buscar_SemPermissaoMostraOsGlobaisEmOrdemInvertida()
    {
        var (lista, twitch, _) = Criar(TwitchComEmotes(), podeLerEmotes: false);

        var resultado = await lista.BuscarAsync("meucanal");

        Assert.True(resultado.SoGlobais);
        Assert.Empty(twitch.PedidosPara("emotes/user"));
        Assert.Equal(["Kappa", "NovoGlobal"], Assert.Single(resultado.Grupos).Emotes.Select(e => e.Nome));
    }

    [Fact]
    public async Task Buscar_401NaListaDaContaPassaParaOsGlobais()
    {
        var twitch = TwitchComEmotes().Quando("GET", "emotes/user", HttpStatusCode.Unauthorized, """{"message":"Missing scope"}""");
        var (lista, _, _) = Criar(twitch);

        var resultado = await lista.BuscarAsync("meucanal");
        Assert.True(resultado.SoGlobais);
        Assert.Equal("Globais da Twitch", Assert.Single(resultado.Grupos).Titulo);
    }

    [Fact]
    public async Task Buscar_ErroPassaAdiante()
    {
        var twitch = TwitchComEmotes().Quando("GET", "emotes/user", HttpStatusCode.InternalServerError);
        var (lista, _, _) = Criar(twitch);
        await Assert.ThrowsAnyAsync<Exception>(() => lista.BuscarAsync("meucanal"));
        Assert.False(lista.EstaPronta("meucanal"));
    }

    [Fact]
    public async Task Buscar_GuardaPor10MinutosPorCanal()
    {
        var (lista, twitch, avancar) = Criar(TwitchComEmotes());

        var primeira = await lista.BuscarAsync("meucanal");
        avancar(TimeSpan.FromMinutes(9));
        Assert.True(lista.EstaPronta("MeuCanal"));
        Assert.Same(primeira, await lista.BuscarAsync("meucanal"));

        avancar(TimeSpan.FromMinutes(2)); // 11 min
        Assert.False(lista.EstaPronta("meucanal"));
        Assert.NotSame(primeira, await lista.BuscarAsync("meucanal"));
        Assert.Equal(4, twitch.PedidosPara("emotes/user").Count());
    }

    [Fact]
    public async Task Buscar_OutroCanalOuDescartarBuscaDeNovo()
    {
        var (lista, _, _) = Criar(TwitchComEmotes());
        await lista.BuscarAsync("meucanal");
        Assert.False(lista.EstaPronta("outro"));

        lista.Descartar();
        Assert.False(lista.EstaPronta("meucanal"));
    }

    [Fact]
    public async Task Buscar_ReaproveitaABuscaEmAndamento()
    {
        var liberar = new TaskCompletionSource();
        var twitch = TwitchComEmotes();
        var (lista, _, _) = Criar(twitch);
        twitch.Quando("GET", "users?login=meucanal", _ =>
        {
            liberar.Task.Wait();
            return (HttpStatusCode.OK, """{"data":[{"id":"99"}]}""");
        });

        var a = Task.Run(() => lista.BuscarAsync("meucanal"));
        await Task.Delay(100);
        var b = lista.BuscarAsync("meucanal");
        liberar.SetResult();

        Assert.Same(await a, await b);
        Assert.Single(twitch.PedidosPara("users?login=meucanal"));
    }
}
