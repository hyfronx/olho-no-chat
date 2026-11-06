using System.Text.Json;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Overlay;

namespace OlhoNoChat.Testes.Overlay;

/// <summary>
/// O que a página do chat manda para o overlay (só os pedidos conhecidos, bem formados) e as opções que a página do
/// overlay lê.
/// </summary>
public sealed class ContratoDoOverlayTestes
{
    [Fact]
    public void LerDaPagina_UmaLinha()
    {
        var pedido = ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"line","html":"<div class=\"chat_line\">oi</div>"}""");

        Assert.Equal(new ContratoDoOverlay.Pedido(ContratoDoOverlay.TipoDoPedido.Linha, "<div class=\"chat_line\">oi</div>"), pedido);
    }

    [Fact]
    public void LerDaPagina_RemoverELimpar()
    {
        Assert.Equal(new ContratoDoOverlay.Pedido(ContratoDoOverlay.TipoDoPedido.Remover, ".chat_line[data-nick=\"x\"]"),
            ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"remove","selector":".chat_line[data-nick=\"x\"]"}"""));
        Assert.Equal(new ContratoDoOverlay.Pedido(ContratoDoOverlay.TipoDoPedido.Limpar, string.Empty),
            ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"clear"}"""));
    }

    [Fact]
    public void LerDaPagina_SubstituirTrazOSeletorEOHtml()
    {
        Assert.Equal(new ContratoDoOverlay.Pedido(ContratoDoOverlay.TipoDoPedido.Substituir, ".chat_line[data-ov=\"3\"]", "<div class=\"chat_line\">x</div>"),
            ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"replace","selector":".chat_line[data-ov=\"3\"]","html":"<div class=\"chat_line\">x</div>"}"""));
        Assert.Null(ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"replace","selector":".x"}"""));
        Assert.Null(ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"replace","html":"<div></div>"}"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("onc:play-sound")]
    [InlineData("""{"t":"line","html":"x"}""")]
    [InlineData("onc:overlay:")]
    [InlineData("onc:overlay:{")]
    [InlineData("onc:overlay:[]")]
    [InlineData("""onc:overlay:{"t":"outro"}""")]
    [InlineData("""onc:overlay:{"html":"x"}""")]
    [InlineData("""onc:overlay:{"t":"line"}""")]
    [InlineData("""onc:overlay:{"t":"line","html":""}""")]
    [InlineData("""onc:overlay:{"t":"line","html":5}""")]
    [InlineData("""onc:overlay:{"t":"remove","selector":null}""")]
    public void LerDaPagina_OQueNaoEUmPedidoEIgnorado(string? mensagem)
    {
        Assert.Null(ContratoDoOverlay.LerDaPagina(mensagem));
    }

    [Fact]
    public void LerDaPagina_GrandeDemaisEIgnorado()
    {
        string html = new('a', ContratoDoOverlay.TamanhoMaximoDoHtml + 1);
        string seletor = new('a', ContratoDoOverlay.TamanhoMaximoDoSeletor + 1);

        Assert.Null(ContratoDoOverlay.LerDaPagina("onc:overlay:" + JsonSerializer.Serialize(new { t = "line", html })));
        Assert.Null(ContratoDoOverlay.LerDaPagina("onc:overlay:" + JsonSerializer.Serialize(new { t = "remove", selector = seletor })));
    }

    [Fact]
    public void JsonDasOpcoes_PadraoDaPrimeiraInstalacao_Exato()
    {
        Assert.Equal(
            """{"fadeSeconds":30,"delaySeconds":0,"maxLines":12,"fontSize":22,"font":"Segoe UI","bold":true,"outline":"theme","background":"none","backgroundOpacity":0.55,"direction":"bottom","align":"left","animation":"slide","hideGifs":false,"badges":true,"time":false,"platform":false,"hideCommands":true,"commandPrefix":"!","hideBots":true,"bannedWords":[],"links":"replace","maxChars":0,"maxEmotes":0,"notices":{"subs":true,"raids":true,"twitchOther":true,"redemptions":true,"superchats":true,"youtubeMembers":true,"tiktokGifts":true,"tiktokOther":true}}""",
            ContratoDoOverlay.JsonDasOpcoes(new Opcoes()));
    }

    [Fact]
    public void JsonDasOpcoes_CadaOpcaoNoSeuCampo()
    {
        var opcoes = new Opcoes
        {
            OverlaySome = false,
            SegundosDeAtrasoNoOverlay = 5,
            MensagensNoOverlay = 3,
            TamanhoDoTextoNoOverlay = 30,
            FonteDoOverlay = "Verdana",
            NegritoNoOverlay = false,
            ContornoNoOverlay = "soft",
            FundoNoOverlay = "card",
            OpacidadeDoFundoNoOverlay = 80,
            DirecaoDoOverlay = "top",
            AlinhamentoDoOverlay = "right",
            AnimacaoDoOverlay = "none",
            EsconderGifs = true,
            SelosNoOverlay = false,
            HorarioNoOverlay = true,
            PlataformaNoOverlay = true,
            ChatMultiplataforma = true,
            EsconderComandosNoOverlay = false,
            PrefixoDosComandosNoOverlay = "?",
            EsconderBotsNoOverlay = false,
            PalavrasProibidasNoOverlay = [" feio ", "", "chato*"],
            LinksNoOverlay = "hide",
            LimitarTamanhoNoOverlay = true,
            CaracteresNoOverlay = 100,
            LimitarEmotesNoOverlay = true,
            EmotesNoOverlay = 4,
            InscricoesNoOverlay = false,
            RaidsNoOverlay = false,
            OutrosAvisosDaTwitchNoOverlay = false,
            ResgatesNoOverlay = false,
            SuperChatsNoOverlay = false,
            MembrosDoYouTubeNoOverlay = false,
            PresentesDoTikTokNoOverlay = false,
            OutrosAvisosDoTikTokNoOverlay = false,
        };

        Assert.Equal(
            """{"fadeSeconds":0,"delaySeconds":5,"maxLines":3,"fontSize":30,"font":"Verdana","bold":false,"outline":"soft","background":"card","backgroundOpacity":0.8,"direction":"top","align":"right","animation":"none","hideGifs":true,"badges":false,"time":true,"platform":true,"hideCommands":false,"commandPrefix":"?","hideBots":false,"bannedWords":["feio","chato*"],"links":"hide","maxChars":100,"maxEmotes":4,"notices":{"subs":false,"raids":false,"twitchOther":false,"redemptions":false,"superchats":false,"youtubeMembers":false,"tiktokGifts":false,"tiktokOther":false}}""",
            ContratoDoOverlay.JsonDasOpcoes(opcoes));
    }

    [Fact]
    public void JsonDasOpcoes_IconeDaPlataformaSoComOChatMultiplataforma()
    {
        string json = ContratoDoOverlay.JsonDasOpcoes(new Opcoes { PlataformaNoOverlay = true, ChatMultiplataforma = false });

        Assert.False(JsonDocument.Parse(json).RootElement.GetProperty("platform").GetBoolean());
    }

    [Fact]
    public void LerDaPagina_UmaLinhaDeTeste()
    {
        Assert.Equal(new ContratoDoOverlay.Pedido(ContratoDoOverlay.TipoDoPedido.Teste, "<div class=\"chat_line\">x</div>"),
            ContratoDoOverlay.LerDaPagina("""onc:overlay:{"t":"test","html":"<div class=\"chat_line\">x</div>"}"""));
    }
}
