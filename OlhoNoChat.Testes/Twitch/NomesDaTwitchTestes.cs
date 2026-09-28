using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public class NomesDaTwitchTestes
{
    [Theory]
    [InlineData("nome", "nome")]
    [InlineData("  @nome  ", "nome")]
    [InlineData("@@nome", "nome")]
    [InlineData("twitch.tv/nome", "nome")]
    [InlineData("https://www.twitch.tv/nome/videos", "nome")]
    [InlineData("https://www.TWITCH.TV/Nome?x=1", "Nome")]
    [InlineData("twitch.tv/popout/nome/chat?popout=", "nome")]
    [InlineData("https://www.twitch.tv/moderator/nome", "nome")]
    [InlineData("https://www.twitch.tv/embed/nome/chat", "nome")]
    [InlineData("https://www.twitch.tv/popout", "popout")]
    [InlineData("https://twitch.tv/nome#chat", "nome")]
    [InlineData("https://twitch.tv/", "")]
    [InlineData("https://exemplo.com/canais/nome/", "nome")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Extrair_TiraONomeDoQueFoiDigitado(string? digitado, string nome)
    {
        Assert.Equal(nome, NomesDaTwitch.Extrair(digitado));
    }

    [Theory]
    [InlineData("hyfronx", true)]
    [InlineData("Nome_123", true)]
    [InlineData("a", true)]
    [InlineData("abcdefghijklmnopqrstuvwxy", true)] // 25
    [InlineData("abcdefghijklmnopqrstuvwxyz", false)] // 26
    [InlineData("", false)]
    [InlineData("com espaco", false)]
    [InlineData("nome-com-traco", false)]
    [InlineData("ação", false)]
    [InlineData(null, false)]
    public void EhValido_SoLetrasNumerosESublinhado(string? nome, bool valido)
    {
        Assert.Equal(valido, NomesDaTwitch.EhValido(nome));
    }

    [Fact]
    public void DicaDoNomeInvalido_TextoDaTela()
    {
        Assert.Equal("Use o nome como aparece no endereço do canal (twitch.tv/nome): só letras, números e _.", NomesDaTwitch.DicaNomeInvalido);
    }
}
