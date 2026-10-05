using System.Text.Json;
using OlhoNoChat.Chat;
using OlhoNoChat.YouTube;
using YTLiveChat.Contracts.Models;

namespace OlhoNoChat.Testes.YouTube;

public class MensagemParaAPaginaTestes
{
    private static ChatItem Item(params MessagePart[] partes) => new()
    {
        Id = "abc",
        Author = new Author { Name = "@Viewer1", ChannelId = "UC1" },
        Message = partes,
    };

    [Fact]
    public void Texto_LoginSemArrobaEmMinusculas()
    {
        MensagemParaAPagina m = MensagemParaAPagina.De(Item(new TextPart { Text = "oi " }, new TextPart { Text = "chat" }))!;

        Assert.Equal("abc", m.Id);
        Assert.Equal("viewer1", m.Login);
        Assert.Equal("@Viewer1", m.Nome);
        Assert.Equal(string.Empty, m.Papel);
        Assert.Equal(["oi chat"], m.Partes);
        Assert.Null(m.SuperChat);
        Assert.Null(m.Aviso);
    }

    [Fact]
    public void EmojiComum_ViraTexto_EODoCanal_ViraImagem()
    {
        MensagemParaAPagina m = MensagemParaAPagina.De(Item(
            new TextPart { Text = "olha " },
            new EmojiPart { EmojiText = "😂", Url = "https://yt/emoji.png", IsCustomEmoji = false },
            new TextPart { Text = " " },
            new EmojiPart { EmojiText = ":gato:", Alt = "gato", Url = "https://yt/gato.png", IsCustomEmoji = true },
            new TextPart { Text = " fim" }))!;

        Assert.Equal(3, m.Partes.Count);
        Assert.Equal("olha 😂 ", m.Partes[0]);
        Assert.Equal("""{"emote":{"src":"https://yt/gato.png"},"name":":gato:"}""", JsonSerializer.Serialize(m.Partes[1]));
        Assert.Equal(" fim", m.Partes[2]);
    }

    [Fact]
    public void Papel_DonoModeradorMembro()
    {
        ChatItem item = Item(new TextPart { Text = "x" });
        item.IsMembership = true;
        Assert.Equal(MensagemParaAPagina.Membro, MensagemParaAPagina.De(item)!.Papel);
        item.IsModerator = true;
        Assert.Equal(MensagemParaAPagina.Moderador, MensagemParaAPagina.De(item)!.Papel);
        item.IsOwner = true;
        Assert.Equal(MensagemParaAPagina.Dono, MensagemParaAPagina.De(item)!.Papel);
    }

    [Fact]
    public void SuperChat_ValorECorComCerquilha()
    {
        ChatItem item = Item(new TextPart { Text = "valeu" });
        item.Superchat = new Superchat { AmountString = "R$ 10,00", Currency = "BRL", BodyBackgroundColor = "1DE9B6" };

        SuperChatParaAPagina sc = MensagemParaAPagina.De(item)!.SuperChat!;
        Assert.Equal("R$ 10,00", sc.Valor);
        Assert.Equal("#1DE9B6", sc.Cor);
    }

    [Fact]
    public void SuperChatSemTexto_AindaAparece()
    {
        ChatItem item = Item();
        item.Superchat = new Superchat { AmountString = "US$ 5.00", Currency = "USD", BodyBackgroundColor = "zzz" };

        MensagemParaAPagina m = MensagemParaAPagina.De(item)!;
        Assert.Empty(m.Partes);
        Assert.Equal("#1E88E5", m.SuperChat!.Cor); // cor estranha: azul do YouTube
    }

    [Theory]
    [InlineData(MembershipEventType.New, null, null, null, "virou membro do canal!")]
    [InlineData(MembershipEventType.Milestone, 1, null, null, "é membro há 1 mês!")]
    [InlineData(MembershipEventType.Milestone, 12, null, null, "é membro há 12 meses!")]
    [InlineData(MembershipEventType.GiftPurchase, null, 5, null, "deu 5 assinaturas de membro!")]
    [InlineData(MembershipEventType.GiftPurchase, null, 1, null, "deu uma assinatura de membro!")]
    [InlineData(MembershipEventType.GiftRedemption, null, null, "@viewer2", "ganhou uma assinatura de membro de @viewer2!")]
    public void Membros_AvisoEmPortugues(MembershipEventType tipo, int? meses, int? presentes, string? quemDeu, string aviso)
    {
        ChatItem item = Item();
        item.MembershipDetails = new MembershipDetails
        {
            EventType = tipo,
            MilestoneMonths = meses,
            GiftCount = presentes,
            GifterUsername = quemDeu,
        };

        Assert.Equal(aviso, MensagemParaAPagina.De(item)!.Aviso);
    }

    [Fact]
    public void SemNada_NaoMostra() => Assert.Null(MensagemParaAPagina.De(Item()));

    [Fact]
    public void ParaAPagina_JsonComOsNomesDaPagina()
    {
        ChatItem item = Item(new TextPart { Text = "</script> oi" });
        item.IsModerator = true;
        string script = ContratoComAPagina.AdicionarDoYouTube(MensagemParaAPagina.De(item)!);

        Assert.StartsWith("window.oncChat && window.oncChat.addYouTube && window.oncChat.addYouTube({", script);
        Assert.Contains("\"login\":\"viewer1\"", script);
        Assert.Contains("\"role\":\"moderator\"", script);
        Assert.Contains("\"superchat\":null", script);
        Assert.DoesNotContain("</script>", script); // o JSON escapa o < e o >
    }
}
