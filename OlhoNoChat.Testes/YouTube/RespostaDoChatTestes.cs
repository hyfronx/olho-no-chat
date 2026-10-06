using OlhoNoChat.YouTube;
using static OlhoNoChat.Testes.YouTube.AmostrasDoYouTube;

namespace OlhoNoChat.Testes.YouTube;

public class RespostaDoChatTestes
{
    private static ItemDoChat Um(params string[] acoes) => Assert.Single(RespostaDoChat.Ler(Resposta("cont1", acoes)).Itens);

    [Fact]
    public void Texto_IdNomeHoraEPapel()
    {
        var enviada = new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
        ItemDoChat item = Um(Texto("ChwKGkNQYTZz", "oi chat", enviada));

        Assert.Equal("ChwKGkNQYTZz", item.Id);
        Assert.Equal("@Viewer1", item.Nome);
        Assert.Equal(enviada, item.EnviadaEm);
        Assert.Equal(PapelNoChat.Nenhum, item.Papel);
        Assert.Equal([new ParteDoChat("oi chat")], item.Partes);
        Assert.Null(item.SuperChat);
        Assert.Null(item.Membro);
    }

    [Fact]
    public void Emojis_OComumViraOCaractere_EODoCanalTemNomeEImagem()
    {
        ItemDoChat item = Um(Acao("liveChatTextMessageRenderer", """
            "message":{"runs":[{"text":"olha "},
              {"emoji":{"emojiId":"😂","shortcuts":[":face_with_tears_of_joy:"],"searchTerms":["face"],
                "image":{"thumbnails":[{"url":"https://fonts.gstatic.com/s/e/notoemoji/15.1/1f602/72.png"}],"accessibility":{"accessibilityData":{"label":"😂"}}}}},
              {"text":" "},
              {"emoji":{"emojiId":"UCkszU2WH9gy1mb0dV-11UJg/egJ1XufTKYfegwOo57ewAg","shortcuts":[":gato:"],"searchTerms":["gato"],
                "image":{"thumbnails":[{"url":"https://yt3.ggpht.com/gato=w24-h24","width":24},{"url":"https://yt3.ggpht.com/gato=w48-h48","width":48}],
                "accessibility":{"accessibilityData":{"label":"gato"}}},"isCustomEmoji":true}},
              {"text":" fim"}]},
            "authorName":{"simpleText":"@Viewer1"},"id":"e1","timestampUsec":"1791296388499512"
            """));

        Assert.Equal(
            [new ParteDoChat("olha "), new ParteDoChat("😂"), new ParteDoChat(" "), new ParteDoChat(":gato:", "https://yt3.ggpht.com/gato=w48-h48"), new ParteDoChat(" fim")],
            item.Partes);
    }

    [Theory]
    [InlineData(SeloDeMembro, PapelNoChat.Membro)]
    [InlineData(SeloDeModerador, PapelNoChat.Moderador)]
    [InlineData(SeloDeDono, PapelNoChat.Dono)]
    [InlineData("", PapelNoChat.Nenhum)]
    public void Papel_PelosSelos(string selos, PapelNoChat papel) => Assert.Equal(papel, Um(Texto("t1", selos: selos)).Papel);

    [Fact]
    public void Papel_OMaisImportante()
    {
        string selos = """
            ,"authorBadges":[{"liveChatAuthorBadgeRenderer":{"customThumbnail":{"thumbnails":[{"url":"https://yt3.ggpht.com/selo"}]},"tooltip":"Membro (1 ano)"}},
              {"liveChatAuthorBadgeRenderer":{"icon":{"iconType":"MODERATOR"},"tooltip":"Moderador"}},
              {"liveChatAuthorBadgeRenderer":{"icon":{"iconType":"VERIFIED"},"tooltip":"Verificado"}}]
            """;
        Assert.Equal(PapelNoChat.Moderador, Um(Texto("t1", selos: selos)).Papel);
    }

    [Fact]
    public void SuperChat_ValorECorDeFundo()
    {
        ItemDoChat item = Um(Acao("liveChatPaidMessageRenderer", """
            "id":"sc1","timestampUsec":"1791296388499512","authorName":{"simpleText":"@Viewer2"},
            "purchaseAmountText":{"simpleText":"R$ 10,00"},"message":{"runs":[{"text":"valeu pela live"}]},
            "headerBackgroundColor":4278239141,"headerTextColor":4278190080,"bodyBackgroundColor":4280150454,"bodyTextColor":4278190080,
            "authorNameTextColor":2315255808,"timestampColor":2147483648,"isV2Style":true
            """));

        Assert.Equal(new SuperChatDoChat("R$ 10,00", "1DE9B6"), item.SuperChat);
        Assert.Equal([new ParteDoChat("valeu pela live")], item.Partes);
    }

    [Fact]
    public void SuperSticker_ValorECorSemTexto()
    {
        ItemDoChat item = Um(Acao("liveChatPaidStickerRenderer", """
            "id":"st1","timestampUsec":"1791296388499512","authorName":{"simpleText":"@Viewer2"},
            "sticker":{"thumbnails":[{"url":"//lh3.googleusercontent.com/sticker=s40"}],"accessibility":{"accessibilityData":{"label":"gato dançando"}}},
            "purchaseAmountText":{"simpleText":"US$ 2,00"},"backgroundColor":4294947584,"authorNameTextColor":3003121664
            """));

        Assert.Equal(new SuperChatDoChat("US$ 2,00", "FFB300"), item.SuperChat);
        Assert.Empty(item.Partes);
    }

    [Fact]
    public void Membro_Novo_ComOTextoDeBoasVindas()
    {
        ItemDoChat item = Um(Acao("liveChatMembershipItemRenderer", """
            "id":"m1","timestampUsec":"1791296388499512","authorName":{"simpleText":"@Viewer1"},
            "headerSubtext":{"runs":[{"text":"Welcome to "},{"text":"The Fam"},{"text":"!"}]}
            """ + SeloDeMembro));

        Assert.Equal(new EventoDeMembro(TipoDeEventoDeMembro.Novo), item.Membro);
        Assert.Equal(PapelNoChat.Membro, item.Papel);
        Assert.Equal("Welcome to The Fam!", string.Concat(item.Partes.Select(p => p.Texto)));
    }

    [Fact]
    public void Membro_SubiuDeNivel()
    {
        ItemDoChat item = Um(Acao("liveChatMembershipItemRenderer", """
            "id":"m2","timestampUsec":"1776280547006206","authorName":{"simpleText":"@Viewer1"},
            "headerSubtext":{"runs":[{"text":"Upgraded membership to "},{"text":"Cardinal Archer"},{"text":"!"}]}
            """));

        Assert.Equal(TipoDeEventoDeMembro.Subiu, item.Membro!.Tipo);
        Assert.Equal(PapelNoChat.Membro, item.Papel); // aviso de membro: membro mesmo sem o selo
    }

    [Fact]
    public void Membro_Marco_MesesEAMensagemDele()
    {
        ItemDoChat item = Um(Acao("liveChatMembershipItemRenderer", """
            "id":"m3","timestampUsec":"1791296388499512","authorName":{"simpleText":"@Viewer1"},
            "headerPrimaryText":{"runs":[{"text":"Membro há "},{"text":"27"},{"text":" meses"}]},
            "headerSubtext":{"simpleText":"The Fam"},"message":{"runs":[{"text":"salve, chat"}]}
            """));

        Assert.Equal(new EventoDeMembro(TipoDeEventoDeMembro.Marco, Meses: 27), item.Membro);
        Assert.Equal([new ParteDoChat("salve, chat")], item.Partes);
    }

    [Fact]
    public void Presente_QuemDeuEstaNoCabecalho()
    {
        ItemDoChat item = Um(Acao("liveChatSponsorshipsGiftPurchaseAnnouncementRenderer", """
            "id":"g1","timestampUsec":"1791296388499512","authorExternalChannelId":"UC_gifter",
            "header":{"liveChatSponsorshipsHeaderRenderer":{"authorName":{"simpleText":"@Viewer2"},
              "authorPhoto":{"thumbnails":[{"url":"https://yt4.ggpht.com/foto"}]},
              "primaryText":{"runs":[{"text":"Sent "},{"text":"5"},{"text":" "},{"text":"The Fam"},{"text":" gift memberships"}]},
              "authorBadges":[{"liveChatAuthorBadgeRenderer":{"icon":{"iconType":"MODERATOR"},"tooltip":"Moderator"}}]}}
            """));

        Assert.Equal("@Viewer2", item.Nome);
        Assert.Equal(PapelNoChat.Moderador, item.Papel);
        Assert.Equal(new EventoDeMembro(TipoDeEventoDeMembro.DeuPresente, Presentes: 5), item.Membro);
        Assert.Empty(item.Partes);
    }

    [Fact]
    public void Presente_UmSemNumero()
    {
        ItemDoChat item = Um(Acao("liveChatSponsorshipsGiftPurchaseAnnouncementRenderer", """
            "id":"g2","timestampUsec":"1791296388499512",
            "header":{"liveChatSponsorshipsHeaderRenderer":{"authorName":{"simpleText":"@Viewer2"},
              "primaryText":{"runs":[{"text":"Gifted a "},{"text":"The Fam"},{"text":" membership"}]}}}
            """));

        Assert.Equal(1, item.Membro!.Presentes);
    }

    [Fact]
    public void GanhouPresente_QuemDeuEOUltimoTexto()
    {
        ItemDoChat item = Um(Acao("liveChatSponsorshipsGiftRedemptionAnnouncementRenderer", """
            "id":"g3","timestampUsec":"1791296388499512","authorName":{"simpleText":"@Viewer1"},
            "message":{"runs":[{"text":"received a gift membership by ","italics":true},{"text":"@viewer3 ","bold":true,"italics":true}]}
            """));

        Assert.Equal(new EventoDeMembro(TipoDeEventoDeMembro.GanhouPresente, QuemDeu: "@viewer3"), item.Membro);
        Assert.Equal(2, item.Partes.Count);
    }

    [Fact]
    public void OutrasAcoes_FicamDeFora()
    {
        RespostaDoChat resposta = RespostaDoChat.Ler(Resposta("cont1",
            """{"clickTrackingParams":"CAEQ","removeChatItemAction":{"targetItemId":"ChwKGkNQYTZz"}}""",
            Acao("liveChatViewerEngagementMessageRenderer", """ "id":"x","message":{"runs":[{"text":"Bem-vindo ao chat ao vivo!"}]} """),
            """{"clickTrackingParams":"CAEQ","addBannerToLiveChatCommand":{"bannerRenderer":{}}}""",
            Texto("t1")));

        Assert.Equal("t1", Assert.Single(resposta.Itens).Id);
    }

    [Fact]
    public void Continuacao_DaInvalidacao_SemEspera()
    {
        RespostaDoChat resposta = RespostaDoChat.Ler(Resposta("cont2"));

        Assert.Equal("cont2", resposta.Continuacao);
        Assert.Null(resposta.Espera); // o timeoutMs dela é para quem recebe as mensagens empurradas
        Assert.Empty(resposta.Itens);
    }

    [Fact]
    public void Continuacao_Temporizada_ComEspera()
    {
        RespostaDoChat resposta = RespostaDoChat.Ler("""
            {"continuationContents":{"liveChatContinuation":{"continuations":[{"timedContinuationData":{"timeoutMs":5000,"continuation":"cont3"}}]}}}
            """);

        Assert.Equal("cont3", resposta.Continuacao);
        Assert.Equal(TimeSpan.FromSeconds(5), resposta.Espera);
    }

    [Theory]
    [InlineData("""{"responseContext":{}}""")]
    [InlineData("""{"continuationContents":{"liveChatContinuation":{"actions":[]}}}""")]
    public void SemContinuacao_ALiveAcabou(string json) => Assert.Null(RespostaDoChat.Ler(json).Continuacao);
}
