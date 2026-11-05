using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Kick;
using OlhoNoChat.TikTok;

namespace OlhoNoChat.Testes.Chat;

/// <summary>
/// O contrato com a página do Padrão (browser/chat.js), que não entra na reescrita: os formatos precisam ficar exatamente
/// iguais.
/// </summary>
public sealed class ContratoComAPaginaTestes
{
    [Fact]
    public void JsonDeConfiguracoes_PadraoDaPrimeiraInstalacao_Exato()
    {
        Assert.Equal(
            """{"fade":0,"hideBots":true,"hideGifs":false,"hideOtherChannels":false,"twitchNotices":true,"youTubeNotices":true,"tiktokGifts":true,"tiktokFollows":true,"tiktokShares":false,"tiktokSuperFans":true,"highlightUsers":false,"allowedUsersOnly":false,"playSound":true,"filterAllowAllVIPs":false,"filterAllowAllMods":false,"vips":[],"blockList":[],"multiplatform":false,"resumeSpeed":0,"pauseHotkey":"Ctrl \u002B Alt \u002B F6"}""",
            ContratoComAPagina.JsonDeConfiguracoes(new Opcoes()));
    }

    [Fact]
    public void JsonDeConfiguracoes_CadaOpcaoNoSeuCampo()
    {
        var opcoes = new Opcoes
        {
            ApagarMensagensAntigas = true,
            SegundosParaApagar = "45",
            EsconderBots = false,
            EsconderGifs = true,
            EsconderOutrosCanais = true,
            MostrarAvisosDaTwitch = false,
            MostrarAvisosDoYouTube = false,
            MostrarPresentesDoTikTok = false,
            MostrarSeguidoresDoTikTok = false,
            MostrarCompartilhamentosDoTikTok = true,
            MostrarSuperfasDoTikTok = false,
            DestacarUsuarios = true,
            SoUsuariosDaLista = true,
            SomDeMensagem = "NONE",
            DestacarVips = true,
            DestacarModeradores = true,
            ListaDeUsuarios = ["Fulano", "BELTRANO"],
            UsuariosBloqueados = ["NightBot"],
            VelocidadeAutomaticaAoContinuar = false,
            LinhasPorSegundoAoContinuar = 2.5,
            AtalhoPausar = null,
        };

        Assert.Equal(
            """{"fade":45,"hideBots":false,"hideGifs":true,"hideOtherChannels":true,"twitchNotices":false,"youTubeNotices":false,"tiktokGifts":false,"tiktokFollows":false,"tiktokShares":true,"tiktokSuperFans":false,"highlightUsers":true,"allowedUsersOnly":true,"playSound":false,"filterAllowAllVIPs":true,"filterAllowAllMods":true,"vips":["fulano","beltrano"],"blockList":["nightbot"],"multiplatform":false,"resumeSpeed":2.5,"pauseHotkey":""}""",
            ContratoComAPagina.JsonDeConfiguracoes(opcoes));
    }

    [Theory]
    [InlineData(false, "120", 0)]
    [InlineData(true, "120", 120)]
    [InlineData(true, "0", 0)]
    [InlineData(true, "-5", 0)]
    [InlineData(true, "abc", 0)]
    [InlineData(true, "", 0)]
    public void Fade_SoComAOpcaoLigadaEUmInteiroPositivo(bool ligado, string segundos, int fade)
    {
        var opcoes = new Opcoes { ApagarMensagensAntigas = ligado, SegundosParaApagar = segundos };

        Assert.StartsWith($"{{\"fade\":{fade},", ContratoComAPagina.JsonDeConfiguracoes(opcoes));
    }

    [Fact]
    public void AlternarPausa_Exato()
    {
        Assert.Equal("window.oncChat && window.oncChat.togglePause && window.oncChat.togglePause();", ContratoComAPagina.AlternarPausa);
    }

    [Fact]
    public void Iniciar_AdicionarAcao_Links_Exatos()
    {
        var opcoes = new Opcoes();
        Assert.Equal($"window.oncChat && window.oncChat.start({ContratoComAPagina.JsonDeConfiguracoes(opcoes)});",
            ContratoComAPagina.Iniciar(opcoes));

        // As aspas saem escapadas (o padrão do System.Text.Json, o mesmo de sempre): o JavaScript lê igual
        Assert.Equal("window.oncChat && window.oncChat.addAction(\"Fulano\", \"#a1b3c4\", \"resgatou \\u0022Hidratar\\u0022 (1.500 pontos)\");",
            ContratoComAPagina.AdicionarAcao("Fulano", "#a1b3c4", "resgatou \"Hidratar\" (1.500 pontos)"));

        Assert.Equal("document.body && document.body.classList.toggle('onc-links-on', true);", ContratoComAPagina.LinksClicaveis(true));
        Assert.Equal("document.body && document.body.classList.toggle('onc-links-on', false);", ContratoComAPagina.LinksClicaveis(false));
    }

    [Fact]
    public void BarrasDeRolagem_SemBordasUmEstiloQueEscondeTodasComBordasSai()
    {
        string esconde = ContratoComAPagina.BarrasDeRolagem(visiveis: false);
        Assert.Contains("scrollbar-width: none !important;", esconde);
        Assert.Contains("*::-webkit-scrollbar { display: none !important; }", esconde);
        Assert.Contains("getElementById('onc-sem-rolagem')) return;", esconde); // posto uma vez só
        Assert.Contains("getElementById('onc-sem-rolagem'); if (estilo) estilo.remove();", ContratoComAPagina.BarrasDeRolagem(visiveis: true));
    }

    [Fact]
    public void ModoRolagem_Exato()
    {
        Assert.Equal(
            "window.oncScrollModeWanted = {\"enabled\":true,\"banner\":true,\"hotkey\":\"Ctrl \\u002B Alt \\u002B F7\"}; if (window.oncSetScrollMode) window.oncSetScrollMode(window.oncScrollModeWanted);",
            ContratoComAPagina.ModoRolagem(true, true, "Ctrl + Alt + F7"));
        Assert.Equal(
            """window.oncScrollModeWanted = {"enabled":false,"banner":false,"hotkey":""}; if (window.oncSetScrollMode) window.oncSetScrollMode(window.oncScrollModeWanted);""",
            ContratoComAPagina.ModoRolagem(false, false, ""));
    }

    [Fact]
    public void AdicionarDoTikTok_TextoEEmotesComoNaTwitch()
    {
        var item = new ItemDoTikTok("123", "42", "viewer1", "Viewer Um", PapelNoTikTok.Moderador,
            [new ParteDoTikTok("oi "), new ParteDoTikTok("", "https://exemplo.com/e.webp")]);

        Assert.Equal(
            """window.oncChat && window.oncChat.addTikTok && window.oncChat.addTikTok({"id":"123","login":"viewer1","name":"Viewer Um","role":"moderator","parts":["oi ",{"emote":{"src":"https://exemplo.com/e.webp"},"name":""}]});""",
            ContratoComAPagina.AdicionarDoTikTok(item));
    }

    [Theory]
    [InlineData(PapelNoTikTok.Dono, "owner")]
    [InlineData(PapelNoTikTok.Assinante, "subscriber")]
    [InlineData(PapelNoTikTok.Nenhum, "")]
    public void AdicionarDoTikTok_Papel(PapelNoTikTok papel, string role)
    {
        var item = new ItemDoTikTok("1", "2", "viewer1", "viewer1", papel, [new ParteDoTikTok("oi")]);

        Assert.Contains($"\"role\":\"{role}\"", ContratoComAPagina.AdicionarDoTikTok(item));
    }

    [Fact]
    public void AdicionarDaKick_TextoEmotesPapeisESelosNaOrdem()
    {
        var item = new ItemDaKick("abc", "viewer1", "Viewer1", "#FF6B6B",
            [new ParteDaKick("boa "), new ParteDaKick("KEKW", "https://files.kick.com/emotes/37226/fullsize")],
            ["moderator"],
            [new SeloDaKick(null, "https://ext.cdn.kick.com/chat/badges/GoldenK_x.png", "GoldenK"), new SeloDaKick("moderator/1", null, "moderator")]);

        Assert.Equal(
            """window.oncChat && window.oncChat.addKick && window.oncChat.addKick({"id":"abc","login":"viewer1","name":"Viewer1","color":"#FF6B6B","roles":["moderator"],"badges":[{"src":"https://ext.cdn.kick.com/chat/badges/GoldenK_x.png","name":"GoldenK"},"moderator/1"],"parts":["boa ",{"emote":{"src":"https://files.kick.com/emotes/37226/fullsize"},"name":"KEKW"}]});""",
            ContratoComAPagina.AdicionarDaKick(item));
    }

    [Fact]
    public void ApagarDaKick_MensagemBanimentoELimpeza()
    {
        Assert.Equal("""window.oncChat && window.oncChat.removeKick && window.oncChat.removeKick({"id":"abc"});""",
            ContratoComAPagina.ApagarDaKick(new EventoDaKick(TipoDeEventoDaKick.Apagada, Id: "abc")));
        Assert.Equal("""window.oncChat && window.oncChat.removeKick && window.oncChat.removeKick({"login":"viewer2"});""",
            ContratoComAPagina.ApagarDaKick(new EventoDaKick(TipoDeEventoDaKick.Banido, Login: "viewer2")));
        Assert.Equal("""window.oncChat && window.oncChat.removeKick && window.oncChat.removeKick({"all":true});""",
            ContratoComAPagina.ApagarDaKick(new EventoDaKick(TipoDeEventoDaKick.Limpeza)));
        Assert.Null(ContratoComAPagina.ApagarDaKick(new EventoDaKick(TipoDeEventoDaKick.Mensagem)));
    }

    [Theory]
    [InlineData(EstadoDaKick.Conectando, "connecting")]
    [InlineData(EstadoDaKick.Conectado, "connected")]
    [InlineData(EstadoDaKick.SemConexao, "failed")]
    [InlineData(EstadoDaKick.CanalNaoExiste, "notfound")]
    [InlineData(EstadoDaKick.Desligado, "off")]
    public void EstadoDaKick_ParaAPagina(EstadoDaKick estado, string esperado)
    {
        Assert.Equal(esperado, ContratoComAPagina.EstadoParaAPagina(estado));
    }

    [Fact]
    public void ApagarDoTikTok_IdsELogins()
    {
        Assert.Equal(
            """window.oncChat && window.oncChat.removeTikTok && window.oncChat.removeTikTok(["1","2"], ["viewer1"]);""",
            ContratoComAPagina.ApagarDoTikTok(["1", "2"], ["viewer1"]));
    }

    [Fact]
    public void PorOCss_CriaOuAtualizaOEstiloDoApp_ComOTextoEmJson()
    {
        string script = ContratoComAPagina.PorOCss("a { content: \"x\"; }\n</style>");

        Assert.Contains("document.getElementById('onc-custom-css')", script);
        Assert.Contains("estilo.id = 'onc-custom-css';", script);
        Assert.Contains("estilo.textContent = css;", script);
        Assert.Contains("})(\"a { content: \\u0022x\\u0022; }\\n\\u003C/style\\u003E\");", script);
    }

    [Theory]
    [InlineData("\"open:1234\"", SaudeDaPagina.Situacao.Aberta, 1234)]
    [InlineData("\"open:200000.5\"", SaudeDaPagina.Situacao.Aberta, 200000.5)]
    [InlineData("\"open:\"", SaudeDaPagina.Situacao.Aberta, 0)]
    [InlineData("\"disconnected\"", SaudeDaPagina.Situacao.Reconectando, 0)]
    [InlineData("\"missing\"", SaudeDaPagina.Situacao.SemScript, 0)]
    [InlineData("null", SaudeDaPagina.Situacao.SemScript, 0)]
    [InlineData("{}", SaudeDaPagina.Situacao.SemScript, 0)]
    [InlineData(null, SaudeDaPagina.Situacao.SemScript, 0)]
    [InlineData("\"error:x is undefined\"", SaudeDaPagina.Situacao.ComErro, 0)]
    public void LerSaude(string? resposta, SaudeDaPagina.Situacao estado, double ms)
    {
        SaudeDaPagina saude = ContratoComAPagina.LerSaude(resposta);

        Assert.Equal(estado, saude.Estado);
        Assert.Equal(ms, saude.SemDados.TotalMilliseconds);
    }

    [Fact]
    public void LerSaude_ErroGuardaOTexto()
    {
        Assert.Equal("error:x is undefined", ContratoComAPagina.LerSaude("\"error:x is undefined\"").Erro);
    }

    [Theory]
    [InlineData("onc:play-sound", MensagemDaPagina.TocarSom)]
    [InlineData("onc:chat-state:connecting", MensagemDaPagina.Conectando)]
    [InlineData("onc:chat-state:connected", MensagemDaPagina.Conectado)]
    [InlineData("onc:chat-state:disconnected", MensagemDaPagina.Desconectado)]
    [InlineData("onc:chat-state:qualquer", MensagemDaPagina.Conectando)]
    [InlineData("onc:exit-scroll-mode", MensagemDaPagina.SairDoModoRolagem)]
    [InlineData("onc:compose-sent", MensagemDaPagina.EscritaEnviada)]
    [InlineData("onc:compose-cancel", MensagemDaPagina.EscritaCancelada)]
    [InlineData("onc:pause-state:paused", MensagemDaPagina.ChatPausado)]
    [InlineData("onc:pause-state:reading", MensagemDaPagina.ChatContinuando)]
    [InlineData("onc:pause-state:live", MensagemDaPagina.ChatAoVivo)]
    [InlineData("onc:pause-state:qualquer", MensagemDaPagina.Desconhecida)]
    [InlineData("onc:outra", MensagemDaPagina.Desconhecida)]
    [InlineData("", MensagemDaPagina.Desconhecida)]
    [InlineData(null, MensagemDaPagina.Desconhecida)]
    public void LerMensagem(string? mensagem, MensagemDaPagina esperada)
    {
        Assert.Equal(esperada, ContratoComAPagina.LerMensagem(mensagem));
    }
}
