using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public class ResgatesDePontosTestes
{
    [Theory]
    [InlineData(1, "1 ponto")]
    [InlineData(0, "0 pontos")]
    [InlineData(2, "2 pontos")]
    [InlineData(1500, "1.500 pontos")]
    [InlineData(1000000, "1.000.000 pontos")]
    public void Pontos_ComSeparadorDeMilharESingular(int custo, string texto)
    {
        Assert.Equal(texto, ResgatesDePontos.Resgate.Pontos(custo));
    }

    [Fact]
    public void Texto_DoResgateNoChat()
    {
        Assert.Equal("resgatou \"Hidratar\" (1.500 pontos)", new ResgatesDePontos.Resgate("Ana", "Hidratar", 1500, "").Texto);
    }

    [Fact]
    public void LerMensagem_BoasVindasEReconexao()
    {
        var boasVindas = ResgatesDePontos.LerMensagem(EventSubFalso.BoasVindas("AQoQILE98gtqShGmLD7AM6yJThAB"))!;
        Assert.Equal("session_welcome", boasVindas.Tipo);
        Assert.Equal("AQoQILE98gtqShGmLD7AM6yJThAB", boasVindas.IdDaSessao);
        Assert.Equal(10, boasVindas.SegundosDeSilencio);
        Assert.Null(boasVindas.EnderecoDeReconexao);

        var reconexao = ResgatesDePontos.LerMensagem(EventSubFalso.Reconectar("S", "wss://eventsub.wss.twitch.tv?x=1"))!;
        Assert.Equal("session_reconnect", reconexao.Tipo);
        Assert.Equal("wss://eventsub.wss.twitch.tv?x=1", reconexao.EnderecoDeReconexao);
        Assert.Null(reconexao.SegundosDeSilencio);
    }

    [Fact]
    public void LerMensagem_ResgateDoExemploDaDocumentacao()
    {
        var mensagem = ResgatesDePontos.LerMensagem(EventSubFalso.Resgate("befa7b53"))!;
        Assert.Equal("notification", mensagem.Tipo);
        Assert.Equal("befa7b53", mensagem.Id);
        Assert.Equal(new ResgatesDePontos.Resgate("Cooler_User", "title", 100, "pogchamp"), mensagem.Resgate);
    }

    [Fact]
    public void LerMensagem_OutrosTiposEFormatosEstranhos()
    {
        Assert.Equal("session_keepalive", ResgatesDePontos.LerMensagem(EventSubFalso.EstouVivo("k1"))!.Tipo);
        Assert.Equal("authorization_revoked", ResgatesDePontos.LerMensagem(EventSubFalso.Revogacao())!.MotivoDaRevogacao);

        // Um evento de outro tipo não vira resgate
        var seguir = ResgatesDePontos.LerMensagem("""
            {"metadata":{"message_id":"f","message_type":"notification","subscription_type":"channel.follow"},
             "payload":{"subscription":{"type":"channel.follow"},"event":{"user_name":"x"}}}
            """)!;
        Assert.Null(seguir.Resgate);

        Assert.Null(ResgatesDePontos.LerMensagem("não é json"));
        Assert.Null(ResgatesDePontos.LerMensagem("""{"outra":"coisa"}"""));
        Assert.Null(ResgatesDePontos.LerMensagem("[1,2]"));
    }

    [Fact]
    public void Espera_CresceAteUmMinuto()
    {
        for (int tentativa = 0; tentativa < 7; tentativa++)
        {
            TimeSpan espera = ResgatesDePontos.EsperaPadrao(tentativa);
            double base_ = Math.Min(1000 * Math.Pow(2, tentativa), 60_000);
            Assert.InRange(espera.TotalMilliseconds, base_, Math.Min(base_ + 999, 60_000));
        }
        Assert.Equal(TimeSpan.FromSeconds(60), ResgatesDePontos.EsperaPadrao(6));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "", "A conexão com a Twitch expirou. Conecte a conta de novo, acima.")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"Forbidden","status":403,"message":"subscription missing proper authorization"}""",
        "A Twitch não deixou ler os resgates do seu canal (403: subscription missing proper authorization). Os pontos do canal só existem em canais de afiliados e parceiros; se o seu tem, desconecte e conecte a conta de novo.")]
    [InlineData(HttpStatusCode.BadRequest, """{"status":400,"message":"invalid transport"}""", "A Twitch recusou mostrar os resgates do seu canal (400: invalid transport).")]
    [InlineData(HttpStatusCode.InternalServerError, "", "A Twitch recusou mostrar os resgates do seu canal (500).")]
    public void TextoDaRecusa_ParaAAbaTwitch(HttpStatusCode codigo, string corpo, string texto)
    {
        Assert.Equal(texto, ResgatesDePontos.TextoDaRecusa(new ApiDaTwitch.Resposta(codigo, corpo)));
    }

    // --- Com o servidor falso ---------------------------------------------------------------------------

    private sealed class Cenario : IDisposable
    {
        public EventSubFalso Servidor { get; } = new();
        public TwitchFalsa Twitch { get; } = new();
        public ContaSalvaNaMemoria Salva { get; } = Montar.ContaConectada();
        public ResgatesDePontos Resgates { get; }
        public List<ResgatesDePontos.Resgate> Recebidos { get; } = [];

        public Cenario(Uri? endereco = null)
        {
            Twitch.Quando("POST", "eventsub/subscriptions", HttpStatusCode.Accepted, """{"data":[{"status":"enabled"}]}""")
                  .Quando("GET", "validate", HttpStatusCode.OK, Montar.ValidacaoOk);
            var api = new ApiDaTwitch(Twitch);
            var conta = new ContaDaTwitch(api, Salva, NullLogger<ContaDaTwitch>.Instance);
            Resgates = new ResgatesDePontos(api, conta, NullLogger<ResgatesDePontos>.Instance, endereco ?? Servidor.Endereco,
                _ => TimeSpan.FromMilliseconds(10));
            Resgates.Resgatado += r => { lock (Recebidos) Recebidos.Add(r); };
        }

        public IReadOnlyList<TwitchFalsa.Pedido> Assinaturas => Twitch.PedidosPara("eventsub/subscriptions");

        /// <summary>Liga e dá as boas-vindas; volta quando a assinatura foi feita.</summary>
        public async Task<WebSocket> LigarAsync(string sessao = "S1")
        {
            Task ligando = Resgates.LigarAsync();
            var (ws, _) = await Servidor.ProximaConexaoAsync();
            await EventSubFalso.MandarAsync(ws, EventSubFalso.BoasVindas(sessao));
            await ligando.WaitAsync(TimeSpan.FromSeconds(10));
            return ws;
        }

        public void Dispose()
        {
            Resgates.Desligar();
            Servidor.Dispose();
        }
    }

    [Fact]
    public async Task Ligar_AssinaSoComOCanalDaContaNaSessaoDasBoasVindas()
    {
        using var c = new Cenario();
        await c.LigarAsync("AQoQILE98gtqShGmLD7AM6yJThAB");

        var assinatura = Assert.Single(c.Assinaturas);
        var json = JsonNode.Parse(assinatura.Corpo)!;
        Assert.Equal("channel.channel_points_custom_reward_redemption.add", (string?)json["type"]);
        Assert.Equal("1", (string?)json["version"]);
        var condicao = json["condition"]!.AsObject();
        Assert.Equal(["broadcaster_user_id"], condicao.Select(p => p.Key)); // sem moderator_user_id
        Assert.Equal("121292674", (string?)condicao["broadcaster_user_id"]);
        Assert.Equal("websocket", (string?)json["transport"]!["method"]);
        Assert.Equal("AQoQILE98gtqShGmLD7AM6yJThAB", (string?)json["transport"]!["session_id"]);
        Assert.Equal("Bearer tok", assinatura.Autorizacao);
        Assert.True(c.Resgates.EstaLigado);
        Assert.Equal("", c.Resgates.Problema);
    }

    [Fact]
    public async Task Resgate_ChegaUmaVezSo()
    {
        using var c = new Cenario();
        var ws = await c.LigarAsync();

        await EventSubFalso.MandarAsync(ws, EventSubFalso.EstouVivo("k1"));
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Resgate("m1", "Ana", "Hidratar", 1500, ""));
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Resgate("m1", "Ana", "Hidratar", 1500, "")); // repetida
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Resgate("m2", "Bia", "Música", 1, "toca aquela"));

        await ContaDaTwitchTestes.EsperarAsync(() => c.Recebidos.Count >= 2);
        await Task.Delay(100);
        Assert.Equal([new("Ana", "Hidratar", 1500, ""), new("Bia", "Música", 1, "toca aquela")], c.Recebidos);
    }

    [Fact]
    public async Task Ligar_DeNovoComOMesmoAcessoNaoMudaNada()
    {
        using var c = new Cenario();
        await c.LigarAsync();
        await c.Resgates.LigarAsync();
        await Task.Delay(100);
        Assert.False(c.Servidor.TemConexaoEsperando);
        Assert.Single(c.Assinaturas);
    }

    [Fact]
    public async Task Reconexao_PedidaPelaTwitchTrocaDeConexaoSemAssinarDeNovo()
    {
        using var c = new Cenario();
        var antiga = await c.LigarAsync();

        await EventSubFalso.MandarAsync(antiga, EventSubFalso.Reconectar("S1", $"ws://localhost:{c.Servidor.Porta}/ws?reconectar=1"));
        var (nova, endereco) = await c.Servidor.ProximaConexaoAsync();
        Assert.Equal("/ws?reconectar=1", endereco);

        // Até a nova dar as boas-vindas, a antiga continua recebendo
        await EventSubFalso.MandarAsync(antiga, EventSubFalso.Resgate("m1", "Ana", "Antes", 5, ""));
        await ContaDaTwitchTestes.EsperarAsync(() => c.Recebidos.Count == 1);

        await EventSubFalso.MandarAsync(nova, EventSubFalso.BoasVindas("S1"));
        Assert.True(await EventSubFalso.FoiFechadaAsync(antiga, TimeSpan.FromSeconds(5)));

        await EventSubFalso.MandarAsync(nova, EventSubFalso.Resgate("m2", "Bia", "Depois", 5, ""));
        await ContaDaTwitchTestes.EsperarAsync(() => c.Recebidos.Count == 2);
        Assert.Single(c.Assinaturas);
    }

    [Fact]
    public async Task Queda_TentaDeNovoEAssinaNaSessaoNova()
    {
        using var c = new Cenario();
        var primeira = await c.LigarAsync("S1");

        primeira.Abort();
        var (segunda, _) = await c.Servidor.ProximaConexaoAsync();
        await EventSubFalso.MandarAsync(segunda, EventSubFalso.BoasVindas("S2"));

        await ContaDaTwitchTestes.EsperarAsync(() => c.Assinaturas.Count == 2);
        Assert.Contains("\"S2\"", c.Assinaturas[1].Corpo);

        await EventSubFalso.MandarAsync(segunda, EventSubFalso.Resgate("m1"));
        await ContaDaTwitchTestes.EsperarAsync(() => c.Recebidos.Count == 1);
    }

    [Fact]
    public async Task Queda_DesisteDepoisDe7Tentativas()
    {
        using var c = new Cenario();
        var primeira = await c.LigarAsync();

        // Cada nova tentativa conecta, mas nunca recebe as boas-vindas: conta como falha
        c.Servidor.FecharTudo = true;
        primeira.Abort();

        await ContaDaTwitchTestes.EsperarAsync(() => !c.Resgates.EstaLigado, segundos: 10);
        Assert.Equal(1 + 7, c.Servidor.Conexoes);
    }

    [Fact]
    public async Task Ligar_SemServidorDesistePorEnquanto()
    {
        using var c = new Cenario(new Uri("ws://localhost:1/ws"));
        await c.Resgates.LigarAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(c.Resgates.EstaLigado);
        Assert.Empty(c.Assinaturas);
        Assert.Equal("", c.Resgates.Problema); // falta de internet não é recusa da Twitch
    }

    [Fact]
    public async Task Recusa_DaTwitchApareceComoProblemaEDesliga()
    {
        using var c = new Cenario();
        c.Twitch.Quando("POST", "eventsub/subscriptions", HttpStatusCode.Forbidden, """{"status":403,"message":"subscription missing proper authorization"}""");
        int avisos = 0;
        c.Resgates.ProblemaMudou += () => avisos++;

        var ws = await c.LigarAsync();

        Assert.StartsWith("A Twitch não deixou ler os resgates do seu canal (403", c.Resgates.Problema);
        Assert.Equal(1, avisos);
        Assert.True(await EventSubFalso.FoiFechadaAsync(ws, TimeSpan.FromSeconds(5)));
        await ContaDaTwitchTestes.EsperarAsync(() => !c.Resgates.EstaLigado);

        c.Resgates.Desligar(); // a opção desligada esquece o aviso
        Assert.Equal("", c.Resgates.Problema);
    }

    [Fact]
    public async Task Revogacao_ParaDeReceber()
    {
        using var c = new Cenario();
        var ws = await c.LigarAsync();

        await EventSubFalso.MandarAsync(ws, EventSubFalso.Revogacao("user_removed"));
        Assert.True(await EventSubFalso.FoiFechadaAsync(ws, TimeSpan.FromSeconds(5)));
        await ContaDaTwitchTestes.EsperarAsync(() => !c.Resgates.EstaLigado);
        await Task.Delay(100);
        Assert.False(c.Servidor.TemConexaoEsperando); // não tenta de novo
    }

    [Fact]
    public async Task Desligar_FechaAConexao()
    {
        using var c = new Cenario();
        var ws = await c.LigarAsync();

        c.Resgates.Desligar();
        Assert.False(c.Resgates.EstaLigado);
        Assert.True(await EventSubFalso.FoiFechadaAsync(ws, TimeSpan.FromSeconds(5)));
        await Task.Delay(100);
        Assert.False(c.Servidor.TemConexaoEsperando);
    }

    [Fact]
    public async Task Ligar_ComAcessoNovoAssinaDeNovo()
    {
        using var c = new Cenario();
        var antiga = await c.LigarAsync("S1");

        c.Salva.Token = "tok2"; // "Conectar de novo"
        Task ligando = c.Resgates.LigarAsync();
        var (nova, _) = await c.Servidor.ProximaConexaoAsync();
        await EventSubFalso.MandarAsync(nova, EventSubFalso.BoasVindas("S2"));
        await ligando.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await EventSubFalso.FoiFechadaAsync(antiga, TimeSpan.FromSeconds(5)));
        Assert.Equal("Bearer tok2", c.Assinaturas[1].Autorizacao);
    }
}
