using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public class EventSubDaTwitchTestes
{
    private static readonly EventSubDaTwitch.Pedido SoResgates = new(true, null);
    private static readonly EventSubDaTwitch.Pedido SoAutoMod = new(false, "777");

    [Theory]
    [InlineData(1, "1 ponto")]
    [InlineData(0, "0 pontos")]
    [InlineData(2, "2 pontos")]
    [InlineData(1500, "1.500 pontos")]
    [InlineData(1000000, "1.000.000 pontos")]
    public void Pontos_ComSeparadorDeMilharESingular(int custo, string texto)
    {
        Assert.Equal(texto, EventSubDaTwitch.Resgate.Pontos(custo));
    }

    [Fact]
    public void Texto_DoResgateNoChat()
    {
        Assert.Equal("resgatou \"Hidratar\" (1.500 pontos)", new EventSubDaTwitch.Resgate("Ana", "Hidratar", 1500, "").Texto);
    }

    [Fact]
    public void LerMensagem_BoasVindasEReconexao()
    {
        var boasVindas = EventSubDaTwitch.LerMensagem(EventSubFalso.BoasVindas("AQoQILE98gtqShGmLD7AM6yJThAB"))!;
        Assert.Equal("session_welcome", boasVindas.Tipo);
        Assert.Equal("AQoQILE98gtqShGmLD7AM6yJThAB", boasVindas.IdDaSessao);
        Assert.Equal(10, boasVindas.SegundosDeSilencio);
        Assert.Null(boasVindas.EnderecoDeReconexao);

        var reconexao = EventSubDaTwitch.LerMensagem(EventSubFalso.Reconectar("S", "wss://eventsub.wss.twitch.tv?x=1"))!;
        Assert.Equal("session_reconnect", reconexao.Tipo);
        Assert.Equal("wss://eventsub.wss.twitch.tv?x=1", reconexao.EnderecoDeReconexao);
        Assert.Null(reconexao.SegundosDeSilencio);
    }

    [Fact]
    public void LerMensagem_ResgateDoExemploDaDocumentacao()
    {
        var mensagem = EventSubDaTwitch.LerMensagem(EventSubFalso.Resgate("befa7b53"))!;
        Assert.Equal("notification", mensagem.Tipo);
        Assert.Equal("befa7b53", mensagem.Id);
        Assert.Equal(new EventSubDaTwitch.Resgate("Cooler_User", "title", 100, "pogchamp"), mensagem.Resgate);
    }

    [Fact]
    public void LerMensagem_OutrosTiposEFormatosEstranhos()
    {
        Assert.Equal("session_keepalive", EventSubDaTwitch.LerMensagem(EventSubFalso.EstouVivo("k1"))!.Tipo);
        Assert.Equal("authorization_revoked", EventSubDaTwitch.LerMensagem(EventSubFalso.Revogacao())!.MotivoDaRevogacao);

        // Um evento de outro tipo não vira resgate
        var seguir = EventSubDaTwitch.LerMensagem("""
            {"metadata":{"message_id":"f","message_type":"notification","subscription_type":"channel.follow"},
             "payload":{"subscription":{"type":"channel.follow"},"event":{"user_name":"x"}}}
            """)!;
        Assert.Null(seguir.Resgate);

        Assert.Null(EventSubDaTwitch.LerMensagem("não é json"));
        Assert.Null(EventSubDaTwitch.LerMensagem("""{"outra":"coisa"}"""));
        Assert.Null(EventSubDaTwitch.LerMensagem("[1,2]"));
    }

    [Fact]
    public void Espera_CresceAteUmMinuto()
    {
        for (int tentativa = 0; tentativa < 7; tentativa++)
        {
            TimeSpan espera = EventSubDaTwitch.EsperaPadrao(tentativa);
            double base_ = Math.Min(1000 * Math.Pow(2, tentativa), 60_000);
            Assert.InRange(espera.TotalMilliseconds, base_, Math.Min(base_ + 999, 60_000));
        }
        Assert.Equal(TimeSpan.FromSeconds(60), EventSubDaTwitch.EsperaPadrao(6));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "", "A conexão com a Twitch expirou. Conecte a conta de novo, acima.")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"Forbidden","status":403,"message":"subscription missing proper authorization"}""",
        "A Twitch não deixou ler os resgates do seu canal (403: subscription missing proper authorization). Os pontos do canal só existem em canais de afiliados e parceiros; se o seu tem, desconecte e conecte a conta de novo.")]
    [InlineData(HttpStatusCode.BadRequest, """{"status":400,"message":"invalid transport"}""", "A Twitch recusou mostrar os resgates do seu canal (400: invalid transport).")]
    [InlineData(HttpStatusCode.InternalServerError, "", "A Twitch recusou mostrar os resgates do seu canal (500).")]
    public void TextoDaRecusa_ParaAAbaTwitch(HttpStatusCode codigo, string corpo, string texto)
    {
        Assert.Equal(texto, EventSubDaTwitch.TextoDaRecusa(new ApiDaTwitch.Resposta(codigo, corpo)));
    }

    // --- Com o servidor falso ---------------------------------------------------------------------------

    private sealed class Cenario : IDisposable
    {
        public EventSubFalso Servidor { get; } = new();
        public TwitchFalsa Twitch { get; } = new();
        public ContaSalvaNaMemoria Salva { get; } = Montar.ContaConectada();
        public EventSubDaTwitch Resgates { get; }
        public List<EventSubDaTwitch.Resgate> Recebidos { get; } = [];

        public Cenario(Uri? endereco = null)
        {
            Twitch.Quando("POST", "eventsub/subscriptions", HttpStatusCode.Accepted, """{"data":[{"status":"enabled"}]}""")
                  .Quando("GET", "validate", HttpStatusCode.OK, Montar.ValidacaoOk);
            var api = new ApiDaTwitch(Twitch);
            var conta = new ContaDaTwitch(api, Salva, NullLogger<ContaDaTwitch>.Instance);
            Resgates = new EventSubDaTwitch(api, conta, NullLogger<EventSubDaTwitch>.Instance, endereco ?? Servidor.Endereco,
                _ => TimeSpan.FromMilliseconds(10));
            Resgates.Resgatado += r => { lock (Recebidos) Recebidos.Add(r); };
            Resgates.MensagemFoiRetida += m => { lock (Retidas) Retidas.Add(m); };
            Resgates.MensagemDecidida += d => { lock (Decididas) Decididas.Add(d); };
        }

        public List<EventSubDaTwitch.MensagemRetida> Retidas { get; } = [];
        public List<EventSubDaTwitch.Decisao> Decididas { get; } = [];

        public IReadOnlyList<TwitchFalsa.Pedido> Assinaturas => Twitch.PedidosPara("eventsub/subscriptions");

        /// <summary>Liga e dá as boas-vindas; volta quando a assinatura foi feita.</summary>
        public async Task<WebSocket> LigarAsync(string sessao = "S1", EventSubDaTwitch.Pedido? pedido = null)
        {
            Task ligando = Resgates.LigarAsync(pedido ?? SoResgates);
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
        await c.Resgates.LigarAsync(SoResgates);
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
        await c.Resgates.LigarAsync(SoResgates).WaitAsync(TimeSpan.FromSeconds(20));

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
        Task ligando = c.Resgates.LigarAsync(SoResgates);
        var (nova, _) = await c.Servidor.ProximaConexaoAsync();
        await EventSubFalso.MandarAsync(nova, EventSubFalso.BoasVindas("S2"));
        await ligando.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await EventSubFalso.FoiFechadaAsync(antiga, TimeSpan.FromSeconds(5)));
        Assert.Equal("Bearer tok2", c.Assinaturas[1].Autorizacao);
    }

    // --- AutoMod ----------------------------------------------------------------------------------------

    [Fact]
    public void LerMensagem_RetidaDoExemploDaDocumentacao()
    {
        var retida = EventSubDaTwitch.LerMensagem(EventSubFalso.Retida("h1"))!.Retida!;
        Assert.Equal("bad-message-id", retida.Id);
        Assert.Equal("viewer1", retida.Login);
        Assert.Equal("Viewer1", retida.Nome);
        Assert.Equal([new("você é feio "), new("Kappa", "25")], retida.Partes);
        Assert.Equal("você é feio Kappa", retida.Texto);
        Assert.Equal("automod", retida.Motivo);
        Assert.Equal("aggressive", retida.Categoria);
    }

    [Fact]
    public void LerMensagem_DecisaoComOStatusEmMinusculas()
    {
        var decisao = EventSubDaTwitch.LerMensagem(EventSubFalso.Decisao("u1", "m9", "Denied"))!.Decidida;
        Assert.Equal(new EventSubDaTwitch.Decisao("m9", "denied"), decisao);
        var revogacao = EventSubDaTwitch.LerMensagem(EventSubFalso.Revogacao("moderator_removed", "automod.message.hold"))!;
        Assert.Equal("automod.message.hold", revogacao.TipoDaAssinatura);
        Assert.Equal("moderator_removed", revogacao.MotivoDaRevogacao);
    }

    [Fact]
    public void LerMensagem_RetidaSemPedacosUsaOTextoESemIdNaoServe()
    {
        var semPedacos = EventSubDaTwitch.LerMensagem("""
            {"metadata":{"message_id":"h1","message_type":"notification"},
             "payload":{"subscription":{"type":"automod.message.hold"},
                        "event":{"user_login":"viewer1","message_id":"m1","message":{"text":"você é feio","fragments":[]},"reason":"blocked_term"}}}
            """)!.Retida!;
        Assert.Equal([new EventSubDaTwitch.ParteDaMensagem("você é feio")], semPedacos.Partes);
        Assert.Equal("viewer1", semPedacos.Nome); // sem user_name, o login
        Assert.Null(semPedacos.Categoria);
        Assert.Null(EventSubDaTwitch.LerMensagem(EventSubFalso.Retida("h2", idDaMensagem: ""))!.Retida);
    }

    [Fact]
    public async Task AutoMod_AssinaAsDuasComoModeradorEEntregaRetidaEDecisao()
    {
        using var c = new Cenario();
        var ws = await c.LigarAsync("S1", new EventSubDaTwitch.Pedido(true, "777"));

        Assert.Equal(3, c.Assinaturas.Count);
        var tipos = c.Assinaturas.Select(a => (string?)JsonNode.Parse(a.Corpo)!["type"]).ToList();
        Assert.Equal(["channel.channel_points_custom_reward_redemption.add", "automod.message.hold", "automod.message.update"], tipos);
        var hold = JsonNode.Parse(c.Assinaturas[1].Corpo)!;
        Assert.Equal("2", (string?)hold["version"]);
        Assert.Equal("777", (string?)hold["condition"]!["broadcaster_user_id"]);
        Assert.Equal("121292674", (string?)hold["condition"]!["moderator_user_id"]);
        Assert.Equal(EventSubDaTwitch.SituacaoDoAutoMod.Funcionando, c.Resgates.AutoMod);

        await EventSubFalso.MandarAsync(ws, EventSubFalso.Retida("h1", "m1"));
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Decisao("u1", "m1", "Expired"));
        await ContaDaTwitchTestes.EsperarAsync(() => c.Retidas.Count == 1 && c.Decididas.Count == 1);
        Assert.Equal("m1", c.Retidas[0].Id);
        Assert.Equal(new EventSubDaTwitch.Decisao("m1", "expired"), c.Decididas[0]);
    }

    [Fact]
    public async Task AutoMod_SemSerModeradorOsResgatesContinuam()
    {
        using var c = new Cenario();
        c.Twitch.Quando("POST", "eventsub/subscriptions", p => p.Corpo.Contains("automod")
            ? (HttpStatusCode.Forbidden, """{"status":403,"message":"subscription missing proper authorization"}""")
            : (HttpStatusCode.Accepted, """{"data":[{"status":"enabled"}]}"""));
        var ws = await c.LigarAsync("S1", new EventSubDaTwitch.Pedido(true, "777"));

        Assert.Equal(2, c.Assinaturas.Count); // a do update nem é tentada
        Assert.True(c.Resgates.EstaLigado);
        Assert.Equal("", c.Resgates.Problema); // não ser moderador não é problema dos resgates
        Assert.Equal(EventSubDaTwitch.SituacaoDoAutoMod.NaoModera, c.Resgates.AutoMod);
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Resgate("m1"));
        await ContaDaTwitchTestes.EsperarAsync(() => c.Recebidos.Count == 1);
    }

    [Fact]
    public async Task AutoMod_SoEleESemSerModeradorFechaAConexao()
    {
        using var c = new Cenario();
        c.Twitch.Quando("POST", "eventsub/subscriptions", HttpStatusCode.Forbidden, """{"status":403}""");
        var ws = await c.LigarAsync("S1", SoAutoMod);

        Assert.True(await EventSubFalso.FoiFechadaAsync(ws, TimeSpan.FromSeconds(5)));
        await ContaDaTwitchTestes.EsperarAsync(() => !c.Resgates.EstaLigado);
        Assert.Equal("", c.Resgates.Problema);
    }

    [Fact]
    public async Task AutoMod_ContaDeixaDeSerModeradoraEOsResgatesContinuam()
    {
        using var c = new Cenario();
        var ws = await c.LigarAsync("S1", new EventSubDaTwitch.Pedido(true, "777"));

        await EventSubFalso.MandarAsync(ws, EventSubFalso.Revogacao("moderator_removed", "automod.message.hold", "rv1"));
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Resgate("m1"));
        await ContaDaTwitchTestes.EsperarAsync(() => c.Recebidos.Count == 1);
        Assert.True(c.Resgates.EstaLigado);
        Assert.Equal(EventSubDaTwitch.SituacaoDoAutoMod.NaoModera, c.Resgates.AutoMod);

        // Sem nada mais assinado, fecha
        await EventSubFalso.MandarAsync(ws, EventSubFalso.Revogacao("user_removed", "channel.channel_points_custom_reward_redemption.add", "rv2"));
        Assert.True(await EventSubFalso.FoiFechadaAsync(ws, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Ligar_OutroCanalDoAutoModAbreSessaoNova()
    {
        using var c = new Cenario();
        var antiga = await c.LigarAsync("S1", SoAutoMod);

        Task ligando = c.Resgates.LigarAsync(new EventSubDaTwitch.Pedido(false, "888"));
        var (nova, _) = await c.Servidor.ProximaConexaoAsync();
        await EventSubFalso.MandarAsync(nova, EventSubFalso.BoasVindas("S2"));
        await ligando.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await EventSubFalso.FoiFechadaAsync(antiga, TimeSpan.FromSeconds(5)));
        Assert.Contains("\"888\"", c.Assinaturas[^1].Corpo);
        Assert.Contains("\"S2\"", c.Assinaturas[^1].Corpo);
    }

    [Fact]
    public async Task Ligar_PedidoVazioDesliga()
    {
        using var c = new Cenario();
        var ws = await c.LigarAsync();
        await c.Resgates.LigarAsync(new EventSubDaTwitch.Pedido(false, null));
        Assert.False(c.Resgates.EstaLigado);
        Assert.Equal(EventSubDaTwitch.SituacaoDoAutoMod.Desligado, c.Resgates.AutoMod);
        Assert.True(await EventSubFalso.FoiFechadaAsync(ws, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AutoMod_SemInternetNaAssinaturaFalhou()
    {
        using var c = new Cenario();
        c.Twitch.Quando("POST", "eventsub/subscriptions", p => p.Corpo.Contains("automod")
            ? (HttpStatusCode.InternalServerError, "{}")
            : (HttpStatusCode.Accepted, """{"data":[{"status":"enabled"}]}"""));
        await c.LigarAsync("S1", new EventSubDaTwitch.Pedido(true, "777"));
        Assert.Equal(EventSubDaTwitch.SituacaoDoAutoMod.Falhou, c.Resgates.AutoMod);
    }

    [Theory]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.Funcionando, "Funcionando no canal aberto.")]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.NaoModera, "Não funciona no canal aberto: a sua conta não é dona nem moderadora dele.")]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.Desligado, "")]
    public void AbaTwitch_SituacaoDoAutoMod(EventSubDaTwitch.SituacaoDoAutoMod situacao, string texto)
    {
        Assert.Equal(texto, OlhoNoChat.Janelas.Configuracoes.LogicaTwitch.TextoDaSituacao(situacao));
    }
}
