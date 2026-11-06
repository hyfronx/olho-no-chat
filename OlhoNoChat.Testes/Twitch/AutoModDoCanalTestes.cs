using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;
using static OlhoNoChat.Twitch.EventSubDaTwitch;

namespace OlhoNoChat.Testes.Twitch;

public class AutoModDoCanalTestes
{
    private sealed class Cenario
    {
        public TwitchFalsa Twitch { get; } = new();
        public AutoModDoCanal AutoMod { get; }
        public List<string?> Avisos { get; } = [];
        public List<bool> Atalhos { get; } = [];

        public Cenario()
        {
            Twitch.Quando("POST", "moderation/automod/message", HttpStatusCode.NoContent);
            AutoMod = new AutoModDoCanal(new ApiDaTwitch(Twitch), Montar.Conta(Twitch, Montar.ContaConectada()), NullLogger.Instance);
            AutoMod.Mudou += aviso => Avisos.Add(aviso);
            AutoMod.TemMensagensMudou += tem => Atalhos.Add(tem);
            AutoMod.TrocarDeCanal("777");
        }

        public IReadOnlyList<TwitchFalsa.Pedido> Decisoes => Twitch.PedidosPara("moderation/automod/message");
    }

    private static MensagemRetida Retida(string id, string nome = "Viewer1") =>
        new(id, nome.ToLowerInvariant(), nome, [new ParteDaMensagem("texto de " + nome)], "automod", "swearing");

    [Fact]
    public void Fila_PorOrdemDeChegadaSemRepetir()
    {
        var c = new Cenario();
        c.AutoMod.Receber(Retida("a"));
        c.AutoMod.Receber(Retida("b"));
        c.AutoMod.Receber(Retida("a")); // repetida

        Assert.Equal(["a", "b"], c.AutoMod.Mensagens.Select(m => m.Id));
        Assert.Equal("a", c.AutoMod.Primeira!.Id);
        Assert.Equal([true], c.Atalhos); // os atalhos ligam uma vez só
        Assert.Equal([null, null], c.Avisos);
    }

    [Fact]
    public async Task Atalho_DecideAMaisAntigaEDepoisAProxima()
    {
        var c = new Cenario();
        c.AutoMod.Receber(Retida("a", "Ana"));
        c.AutoMod.Receber(Retida("b", "Bia"));

        await c.AutoMod.DecidirAsync(null, aprovar: true);
        await c.AutoMod.DecidirAsync(null, aprovar: false);

        var corpos = c.Decisoes.Select(p => JsonNode.Parse(p.Corpo)!).ToList();
        Assert.Equal(["a", "b"], corpos.Select(j => (string?)j["msg_id"]));
        Assert.Equal(["ALLOW", "DENY"], corpos.Select(j => (string?)j["action"]));
        Assert.All(corpos, j => Assert.Equal("121292674", (string?)j["user_id"]));
        Assert.Empty(c.AutoMod.Mensagens);
        Assert.Contains("Aprovada: Ana", c.Avisos);
        Assert.Contains("Recusada: Bia", c.Avisos);
        Assert.Equal([true, false], c.Atalhos); // e desligam com a fila vazia
    }

    [Fact]
    public async Task Botao_DecideAMensagemDoId()
    {
        var c = new Cenario();
        c.AutoMod.Receber(Retida("a"));
        c.AutoMod.Receber(Retida("b"));

        await c.AutoMod.DecidirAsync("b", aprovar: true);
        await c.AutoMod.DecidirAsync("nao-existe", aprovar: true);

        Assert.Equal(["a"], c.AutoMod.Mensagens.Select(m => m.Id));
        Assert.Single(c.Decisoes);
    }

    [Fact]
    public async Task SemInternet_VoltaParaOComeco()
    {
        var c = new Cenario();
        c.Twitch.Quando("POST", "moderation/automod/message", _ => throw new HttpRequestException("sem internet"));
        c.AutoMod.Receber(Retida("a"));
        c.AutoMod.Receber(Retida("b"));

        await c.AutoMod.DecidirAsync(null, aprovar: true);

        Assert.Equal(["a", "b"], c.AutoMod.Mensagens.Select(m => m.Id));
        Assert.StartsWith("Sem conexão com a Twitch", c.Avisos[^1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 1, "Essa mensagem já tinha saído da fila")]
    [InlineData(HttpStatusCode.Forbidden, 0, "A sua conta não é mais moderadora")]
    [InlineData(HttpStatusCode.InternalServerError, 2, "A Twitch não aceitou agora (500)")]
    public async Task Resposta_DaTwitch(HttpStatusCode codigo, int ficam, string aviso)
    {
        var c = new Cenario();
        c.Twitch.Quando("POST", "moderation/automod/message", codigo, "{}");
        c.AutoMod.Receber(Retida("a"));
        c.AutoMod.Receber(Retida("b"));

        await c.AutoMod.DecidirAsync(null, aprovar: true);

        Assert.Equal(ficam, c.AutoMod.Mensagens.Count);
        Assert.StartsWith(aviso, c.Avisos[^1]);
    }

    [Fact]
    public void Decidida_ForaDoAppTiraDaFila()
    {
        var c = new Cenario();
        c.AutoMod.Receber(Retida("a"));
        c.AutoMod.Receber(Retida("b"));

        c.AutoMod.Decidida(new Decisao("a", "approved"));
        c.AutoMod.Decidida(new Decisao("x", "expired")); // de outra que não está na fila: nada muda

        Assert.Equal(["b"], c.AutoMod.Mensagens.Select(m => m.Id));
        Assert.Equal(3, c.Avisos.Count);
    }

    [Fact]
    public void TrocarDeCanal_EsvaziaASemCanalNaoRecebe()
    {
        var c = new Cenario();
        c.AutoMod.Receber(Retida("a"));
        c.AutoMod.TrocarDeCanal("777"); // o mesmo: nada muda
        Assert.Single(c.AutoMod.Mensagens);

        c.AutoMod.TrocarDeCanal(null);
        Assert.Empty(c.AutoMod.Mensagens);
        Assert.Equal([true, false], c.Atalhos);

        c.AutoMod.Receber(Retida("b"));
        Assert.Empty(c.AutoMod.Mensagens);
    }

    [Fact]
    public void Fila_GuardaNoMaximo100()
    {
        var c = new Cenario();
        for (int i = 0; i < AutoModDoCanal.Maximo + 5; i++)
            c.AutoMod.Receber(Retida("m" + i));
        Assert.Equal(AutoModDoCanal.Maximo, c.AutoMod.Mensagens.Count);
        Assert.Equal("m5", c.AutoMod.Primeira!.Id);
    }

    [Theory]
    [InlineData("blocked_term", null, "termo bloqueado")]
    [InlineData("blocked_link", null, "link bloqueado")]
    [InlineData("automod", "swearing", "palavrão")]
    [InlineData("automod", "Aggressive", "agressividade")]
    [InlineData("automod", "algo_novo", "")]
    [InlineData("automod", null, "")]
    public void Motivo_EmPortugues(string motivo, string? categoria, string texto)
    {
        Assert.Equal(texto, AutoModDoCanal.Motivo(new MensagemRetida("a", "v", "V", [], motivo, categoria)));
    }

    // --- O que vai para a página ------------------------------------------------------------------------

    [Fact]
    public void Contrato_MostraAPrimeiraComOsAtalhos()
    {
        var c = new Cenario();
        c.AutoMod.Receber(new MensagemRetida("m1", "Viewer1", "Viewer1", [new("oi "), new("Kappa", "25")], "automod", "swearing"));
        c.AutoMod.Receber(Retida("m2"));

        string script = ContratoComAPagina.MostrarAutoMod(c.AutoMod, new Atalho(System.Windows.Input.Key.Home,
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt), null, "Aprovada: Ana");

        Assert.StartsWith("window.oncChat && window.oncChat.automod && window.oncChat.automod({", script);
        var json = JsonNode.Parse(script[(script.IndexOf("automod({", StringComparison.Ordinal) + 8)..^2])!;
        Assert.Equal("m1", (string?)json["current"]!["id"]);
        Assert.Equal("viewer1", (string?)json["current"]!["login"]);
        Assert.Equal("Viewer1", (string?)json["current"]!["name"]);
        Assert.Equal("oi ", (string?)json["current"]!["parts"]![0]);
        Assert.Equal("25", (string?)json["current"]!["parts"]![1]!["emoteId"]);
        Assert.Equal("Kappa", (string?)json["current"]!["parts"]![1]!["name"]);
        Assert.Equal("palavrão", (string?)json["current"]!["reason"]);
        Assert.Equal(2, (int?)json["total"]);
        Assert.Equal("Ctrl + Alt + Home", (string?)json["allowHotkey"]);
        Assert.Equal("", (string?)json["denyHotkey"]);
        Assert.Equal("Aprovada: Ana", (string?)json["notice"]);
    }

    [Fact]
    public void Contrato_FilaVazia()
    {
        var c = new Cenario();
        string script = ContratoComAPagina.MostrarAutoMod(c.AutoMod, null, null, null);
        var json = JsonNode.Parse(script[(script.IndexOf("automod({", StringComparison.Ordinal) + 8)..^2])!;
        Assert.Null(json["current"]);
        Assert.Equal(0, (int?)json["total"]);
        Assert.Equal("", (string?)json["notice"]);
    }

    [Theory]
    [InlineData("onc:automod:allow:abc-1", "abc-1", true)]
    [InlineData("onc:automod:deny:abc-1", "abc-1", false)]
    public void Contrato_BotoesDoCartao(string mensagem, string id, bool aprovar)
    {
        Assert.Equal((id, aprovar), ContratoComAPagina.LerPedidoDoAutoMod(mensagem));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("onc:automod:allow:")]
    [InlineData("onc:play-sound")]
    [InlineData("onc:automod:outra:abc")]
    public void Contrato_OutrasMensagensNaoSaoDoAutoMod(string? mensagem)
    {
        Assert.Null(ContratoComAPagina.LerPedidoDoAutoMod(mensagem));
    }

    // --- Opções e aba Twitch ----------------------------------------------------------------------------

    [Fact]
    public void Opcoes_AtalhosNovosEmUsoPorOutroFicamSemAtalho()
    {
        var home = new Atalho(System.Windows.Input.Key.Home, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt);
        var opcoes = new Opcoes { AtalhoBordas = home, AtalhoPausar = new Opcoes().AtalhoRecusarAutoMod };
        opcoes.Corrigir();
        Assert.Null(opcoes.AtalhoAprovarAutoMod);
        Assert.Null(opcoes.AtalhoRecusarAutoMod);

        var padrao = new Opcoes();
        padrao.Corrigir();
        Assert.Equal("Ctrl + Alt + Home", padrao.AtalhoAprovarAutoMod!.ToString());
        Assert.Equal("Ctrl + Alt + End", padrao.AtalhoRecusarAutoMod!.ToString());
        Assert.True(padrao.AprovarAutoMod);
    }

    [Fact]
    public void AbaTwitch_DicaCitaOsAtalhosQueExistem()
    {
        var o = new Opcoes();
        Assert.EndsWith("Com as bordas visíveis, clique em ✓ ou ✕; no jogo, Ctrl + Alt + Home aprova e Ctrl + Alt + End recusa.",
            OlhoNoChat.Janelas.Configuracoes.LogicaTwitch.TextoDaDicaDoAutoMod(o.AtalhoAprovarAutoMod, o.AtalhoRecusarAutoMod));
        Assert.EndsWith("clique em ✓ ou ✕; no jogo, Ctrl + Alt + End recusa.",
            OlhoNoChat.Janelas.Configuracoes.LogicaTwitch.TextoDaDicaDoAutoMod(null, o.AtalhoRecusarAutoMod));
        Assert.EndsWith("clique em ✓ ou ✕.", OlhoNoChat.Janelas.Configuracoes.LogicaTwitch.TextoDaDicaDoAutoMod(null, null));
    }
}
