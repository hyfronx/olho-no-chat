using System.Text.Json;
using OlhoNoChat.Chat;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Testes.YouTube;

public class MensagemParaAPaginaTestes
{
    private static ItemDoChat Item(params ParteDoChat[] partes) => new("abc", DateTimeOffset.UtcNow, "@Viewer1", PapelNoChat.Nenhum, partes);

    [Fact]
    public void Texto_LoginSemArrobaEmMinusculas()
    {
        MensagemParaAPagina m = MensagemParaAPagina.De(Item(new ParteDoChat("oi "), new ParteDoChat("chat")))!;

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
            new ParteDoChat("olha "),
            new ParteDoChat("😂"),
            new ParteDoChat(" "),
            new ParteDoChat(":gato:", "https://yt/gato.png"),
            new ParteDoChat(" fim")))!;

        Assert.Equal(3, m.Partes.Count);
        Assert.Equal("olha 😂 ", m.Partes[0]);
        Assert.Equal("""{"emote":{"src":"https://yt/gato.png"},"name":":gato:"}""", JsonSerializer.Serialize(m.Partes[1]));
        Assert.Equal(" fim", m.Partes[2]);
    }

    [Theory]
    [InlineData(PapelNoChat.Membro, MensagemParaAPagina.Membro)]
    [InlineData(PapelNoChat.Moderador, MensagemParaAPagina.Moderador)]
    [InlineData(PapelNoChat.Dono, MensagemParaAPagina.Dono)]
    [InlineData(PapelNoChat.Nenhum, "")]
    public void Papel_DonoModeradorMembro(PapelNoChat papel, string naPagina) =>
        Assert.Equal(naPagina, MensagemParaAPagina.De(Item(new ParteDoChat("x")) with { Papel = papel })!.Papel);

    [Fact]
    public void SuperChat_ValorECorComCerquilha()
    {
        ItemDoChat item = Item(new ParteDoChat("valeu")) with { SuperChat = new SuperChatDoChat("R$ 10,00", "1DE9B6") };

        SuperChatParaAPagina sc = MensagemParaAPagina.De(item)!.SuperChat!;
        Assert.Equal("R$ 10,00", sc.Valor);
        Assert.Equal("#1DE9B6", sc.Cor);
    }

    [Theory]
    [InlineData("zzz")]
    [InlineData(null)]
    public void SuperChatSemTexto_AindaAparece(string? cor)
    {
        ItemDoChat item = Item() with { SuperChat = new SuperChatDoChat("US$ 5.00", cor) };

        MensagemParaAPagina m = MensagemParaAPagina.De(item)!;
        Assert.Empty(m.Partes);
        Assert.Equal("#1E88E5", m.SuperChat!.Cor); // sem cor ou cor estranha: azul do YouTube
    }

    [Theory]
    [InlineData(TipoDeEventoDeMembro.Novo, null, null, null, "virou membro do canal!")]
    [InlineData(TipoDeEventoDeMembro.Subiu, null, null, null, "subiu de nível como membro!")]
    [InlineData(TipoDeEventoDeMembro.Marco, 1, null, null, "é membro há 1 mês!")]
    [InlineData(TipoDeEventoDeMembro.Marco, 12, null, null, "é membro há 12 meses!")]
    [InlineData(TipoDeEventoDeMembro.DeuPresente, null, 5, null, "deu 5 assinaturas de membro!")]
    [InlineData(TipoDeEventoDeMembro.DeuPresente, null, 1, null, "deu uma assinatura de membro!")]
    [InlineData(TipoDeEventoDeMembro.GanhouPresente, null, null, "@viewer2", "ganhou uma assinatura de membro de @viewer2!")]
    [InlineData(TipoDeEventoDeMembro.GanhouPresente, null, null, null, "ganhou uma assinatura de membro!")]
    public void Membros_AvisoEmPortugues(TipoDeEventoDeMembro tipo, int? meses, int? presentes, string? quemDeu, string aviso)
    {
        ItemDoChat item = Item() with { Membro = new EventoDeMembro(tipo, meses, presentes, quemDeu) };

        Assert.Equal(aviso, MensagemParaAPagina.De(item)!.Aviso);
    }

    [Fact]
    public void SemNada_NaoMostra() => Assert.Null(MensagemParaAPagina.De(Item()));

    [Fact]
    public void ParaAPagina_JsonComOsNomesDaPagina()
    {
        ItemDoChat item = Item(new ParteDoChat("</script> oi")) with { Papel = PapelNoChat.Moderador };
        string script = ContratoComAPagina.AdicionarDoYouTube(MensagemParaAPagina.De(item)!);

        Assert.StartsWith("window.oncChat && window.oncChat.addYouTube && window.oncChat.addYouTube({", script);
        Assert.Contains("\"login\":\"viewer1\"", script);
        Assert.Contains("\"role\":\"moderator\"", script);
        Assert.Contains("\"superchat\":null", script);
        Assert.DoesNotContain("</script>", script); // o JSON escapa o < e o >
    }
}
