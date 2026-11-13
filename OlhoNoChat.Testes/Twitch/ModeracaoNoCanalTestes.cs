using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Chat;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public class ModeracaoNoCanalTestes
{
    private sealed class Cenario
    {
        public TwitchFalsa Twitch { get; } = new();
        public ModeracaoNoCanal Moderacao { get; }
        public int Mudancas { get; private set; }

        public Cenario()
        {
            // A conta conferida (com as permissões de moderar), o canal "gaules" (id 181077473) e a lista de canais
            // moderados em duas páginas
            Twitch.Quando("GET", "oauth2/validate", HttpStatusCode.OK, Montar.ValidacaoOk);
            Twitch.Quando("GET", "helix/users", HttpStatusCode.OK, Montar.PerfilOk);
            Twitch.Quando("GET", "users?login=gaules", HttpStatusCode.OK, """{"data":[{"id":"181077473","login":"gaules"}]}""");
            Twitch.Quando("GET", "users?login=outro", HttpStatusCode.OK, """{"data":[{"id":"999","login":"outro"}]}""");
            Twitch.Quando("GET", "moderation/channels", p => p.Endereco.Contains("after=c2")
                ? (HttpStatusCode.OK, """{"data":[{"broadcaster_id":"181077473","broadcaster_login":"gaules"}],"pagination":{}}""")
                : (HttpStatusCode.OK, """{"data":[{"broadcaster_id":"5","broadcaster_login":"x"}],"pagination":{"cursor":"c2"}}"""));
            ContaDaTwitch conta = Montar.Conta(Twitch, Montar.ContaConectada());
            Assert.True(conta.VerificarAsync().GetAwaiter().GetResult());
            Moderacao = new ModeracaoNoCanal(new ApiDaTwitch(Twitch), conta, NullLogger.Instance);
            Moderacao.Mudou += () => Mudancas++;
        }
    }

    [Fact]
    public async Task CanalDaConta_Dono()
    {
        var c = new Cenario();
        await c.Moderacao.TrocarDeCanalAsync("Hyfronx");

        Assert.Equal(ModeracaoNoCanal.Papel.Dono, c.Moderacao.PapelNoCanal);
        Assert.Equal("121292674", c.Moderacao.IdDoCanal);
        Assert.Empty(c.Twitch.PedidosPara("moderation/channels")); // o dono não precisa da lista
    }

    [Fact]
    public async Task CanalModerado_AchadoNaSegundaPagina_EGuardado()
    {
        var c = new Cenario();
        await c.Moderacao.TrocarDeCanalAsync("gaules");

        Assert.Equal(ModeracaoNoCanal.Papel.Moderador, c.Moderacao.PapelNoCanal);
        Assert.Equal("181077473", c.Moderacao.IdDoCanal);
        Assert.Equal(2, c.Twitch.PedidosPara("moderation/channels").Count);
        Assert.Contains("user_id=121292674", c.Twitch.PedidosPara("moderation/channels")[0].Endereco);

        await c.Moderacao.TrocarDeCanalAsync(null);
        await c.Moderacao.TrocarDeCanalAsync("gaules");
        Assert.Equal(2, c.Twitch.PedidosPara("moderation/channels").Count); // a resposta ficou guardada
        Assert.Equal(3, c.Mudancas);
    }

    [Fact]
    public async Task CanalQueNaoModera_Nenhum()
    {
        var c = new Cenario();
        await c.Moderacao.TrocarDeCanalAsync("outro");

        Assert.Equal(ModeracaoNoCanal.Papel.Nenhum, c.Moderacao.PapelNoCanal);
        Assert.False(c.Moderacao.PodeModerar);
        var (aviso, deu) = await c.Moderacao.ExecutarAsync(new ModeracaoNoCanal.Pedido("delete", "m1", "7", "viewer1", null));
        Assert.False(deu);
        Assert.StartsWith("Só dá para moderar", aviso);
        Assert.Empty(c.Twitch.PedidosPara("moderation/chat"));
    }

    [Fact]
    public async Task SemInternet_Nenhum()
    {
        var c = new Cenario();
        var semRede = new ModeracaoNoCanal(new ApiDaTwitch(new TwitchFalsa()), Montar.Conta(new TwitchFalsa(), Montar.ContaConectada()),
            NullLogger.Instance);
        await semRede.TrocarDeCanalAsync("gaules");
        Assert.Equal(ModeracaoNoCanal.Papel.Nenhum, semRede.PapelNoCanal);
    }

    [Fact]
    public async Task Apagar_ChamaATwitchComOsIds()
    {
        var c = new Cenario();
        c.Twitch.Quando("DELETE", "moderation/chat", HttpStatusCode.NoContent);
        await c.Moderacao.TrocarDeCanalAsync("gaules");

        var (aviso, deu) = await c.Moderacao.ExecutarAsync(new ModeracaoNoCanal.Pedido("delete", "abc-1", "7", "viewer1", null));

        var pedido = Assert.Single(c.Twitch.PedidosPara("moderation/chat"));
        Assert.Contains("broadcaster_id=181077473", pedido.Endereco);
        Assert.Contains("moderator_id=121292674", pedido.Endereco);
        Assert.Contains("message_id=abc-1", pedido.Endereco);
        Assert.True(deu);
        Assert.Equal("Mensagem de viewer1 apagada.", aviso);
    }

    [Fact]
    public async Task Suspender_ComADuracao_EBanir_Sem()
    {
        var c = new Cenario();
        c.Twitch.Quando("POST", "moderation/bans", HttpStatusCode.OK, """{"data":[{}]}""");
        await c.Moderacao.TrocarDeCanalAsync("gaules");

        var suspenso = await c.Moderacao.ExecutarAsync(new ModeracaoNoCanal.Pedido("timeout", "m1", "77", "viewer2", 600));
        var banido = await c.Moderacao.ExecutarAsync(new ModeracaoNoCanal.Pedido("ban", "m1", "78", "viewer3", null));

        var corpos = c.Twitch.PedidosPara("moderation/bans").Select(p => JsonNode.Parse(p.Corpo)!["data"]!).ToList();
        Assert.Equal("77", (string?)corpos[0]["user_id"]);
        Assert.Equal(600, (int)corpos[0]["duration"]!);
        Assert.Equal("78", (string?)corpos[1]["user_id"]);
        Assert.Null(corpos[1]["duration"]);
        Assert.Equal(("viewer2 suspenso por 10 min.", true), suspenso);
        Assert.Equal(("viewer3 banido do chat.", true), banido);
    }

    [Fact]
    public async Task Recusa403_TiraOsBotoes()
    {
        var c = new Cenario();
        c.Twitch.Quando("POST", "moderation/bans", HttpStatusCode.Forbidden, """{"message":"x"}""");
        await c.Moderacao.TrocarDeCanalAsync("gaules");

        var (aviso, deu) = await c.Moderacao.ExecutarAsync(new ModeracaoNoCanal.Pedido("ban", null, "78", "viewer3", null));

        Assert.False(deu);
        Assert.Equal("A sua conta não é mais moderadora deste canal.", aviso);
        Assert.False(c.Moderacao.PodeModerar);
    }

    [Theory]
    [InlineData("delete", HttpStatusCode.BadRequest, "", "A Twitch não deixou apagar: mensagens do dono, de moderadores ou com mais de 6 horas não podem ser apagadas.")]
    [InlineData("delete", HttpStatusCode.NotFound, "", "Essa mensagem já tinha sido apagada.")]
    [InlineData("ban", HttpStatusCode.BadRequest, """{"message":"The user specified in the user_id field is already banned."}""", "viewer1 já está banido.")]
    [InlineData("timeout", HttpStatusCode.BadRequest, """{"message":"may not be banned"}""", "A Twitch não deixou suspender viewer1: o dono do canal e os moderadores não podem ser.")]
    [InlineData("timeout", HttpStatusCode.Conflict, "", "A Twitch pediu para esperar um pouco. Tente de novo.")]
    public void Recusas_EmPortugues(string acao, HttpStatusCode codigo, string corpo, string esperado)
    {
        var pedido = new ModeracaoNoCanal.Pedido(acao, "m1", "7", "viewer1", acao == "timeout" ? 60 : null);
        Assert.Equal(esperado, ModeracaoNoCanal.TextoDaResposta(pedido, new ApiDaTwitch.Resposta(codigo, corpo)));
    }

    [Theory]
    [InlineData(60, "1 min")]
    [InlineData(600, "10 min")]
    [InlineData(3600, "1 hora")]
    [InlineData(7200, "2 horas")]
    [InlineData(86400, "1 dia")]
    [InlineData(604800, "1 semana")]
    [InlineData(30, "30 s")]
    public void Duracao_EmPortugues(int segundos, string esperado) => Assert.Equal(esperado, ModeracaoNoCanal.Duracao(segundos));

    [Fact]
    public void LerPedido_SoOsCompletos()
    {
        Assert.Equal(new ModeracaoNoCanal.Pedido("timeout", "m1", "7", "viewer1", 600),
            ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"timeout","msg":"m1","uid":"7","name":"viewer1","s":600}"""));
        Assert.Equal(new ModeracaoNoCanal.Pedido("delete", "m1", "7", "viewer1", null),
            ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"delete","msg":"m1","uid":"7","name":"viewer1"}"""));
        Assert.NotNull(ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"ban","uid":"7","name":"viewer1"}"""));
        Assert.Null(ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"delete","uid":"7"}""")); // sem a mensagem
        Assert.Null(ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"timeout","uid":"7","s":0}"""));
        Assert.Null(ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"timeout","uid":"7","s":9999999}""")); // mais que 14 dias
        Assert.Null(ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"ban"}"""));
        Assert.Null(ModeracaoNoCanal.LerPedido("""onc:mod:{"a":"unban","uid":"7"}"""));
        Assert.Null(ModeracaoNoCanal.LerPedido("onc:mod:não é json"));
        Assert.Null(ModeracaoNoCanal.LerPedido("onc:play-sound"));
    }

    [Fact]
    public void Pagina_RecebeQuemModera()
    {
        string script = ContratoComAPagina.Moderacao(true, dono: false, "Hyfronx");
        Assert.Contains("""{"can":true,"owner":false,"self":"hyfronx"}""", script);
        Assert.Contains("moderationNotice(\"Mensagem de viewer1 apagada.\", true)",
            ContratoComAPagina.AvisoDaModeracao("Mensagem de viewer1 apagada.", true));
    }

    [Fact]
    public void Novidade_DepoisDaDosAnuncios()
    {
        string[] vistas = [Novidades.Pausa, Novidades.AutoMod, Novidades.Overlay];
        Assert.Equal(Novidades.Anuncios, Novidades.Proxima(vistas, true, false, true, true, false, false, anunciosPronto: true, moderacaoPronta: true));
        Assert.Equal(Novidades.Moderacao, Novidades.Proxima([.. vistas, Novidades.Anuncios], true, false, true, true, false, false,
            anunciosPronto: true, moderacaoPronta: true));
        Assert.Null(Novidades.Proxima([.. vistas, Novidades.Anuncios], true, false, true, true, false, false, moderacaoPronta: false));
    }
}
