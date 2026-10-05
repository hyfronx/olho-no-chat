using OlhoNoChat.YouTube;

namespace OlhoNoChat.Testes.YouTube;

public class CanalDoYouTubeTestes
{
    [Theory]
    [InlineData("@Hyfronx", TipoDeCanalDoYouTube.Arroba, "Hyfronx", "@Hyfronx")]
    [InlineData("  hyfronx ", TipoDeCanalDoYouTube.Arroba, "hyfronx", "@hyfronx")]
    [InlineData("@meu.canal-br_1", TipoDeCanalDoYouTube.Arroba, "meu.canal-br_1", "@meu.canal-br_1")]
    [InlineData("https://www.youtube.com/@LofiGirl", TipoDeCanalDoYouTube.Arroba, "LofiGirl", "@LofiGirl")]
    [InlineData("youtube.com/@LofiGirl/live", TipoDeCanalDoYouTube.Arroba, "LofiGirl", "@LofiGirl")]
    [InlineData("https://m.youtube.com/@LofiGirl/streams?x=1", TipoDeCanalDoYouTube.Arroba, "LofiGirl", "@LofiGirl")]
    [InlineData("UCSJ4gkVC6NrvII8umztf0Ow", TipoDeCanalDoYouTube.Id, "UCSJ4gkVC6NrvII8umztf0Ow", "UCSJ4gkVC6NrvII8umztf0Ow")]
    [InlineData("https://www.youtube.com/channel/UCSJ4gkVC6NrvII8umztf0Ow", TipoDeCanalDoYouTube.Id, "UCSJ4gkVC6NrvII8umztf0Ow", "UCSJ4gkVC6NrvII8umztf0Ow")]
    [InlineData("https://www.youtube.com/watch?v=jfKfPfyJRdk", TipoDeCanalDoYouTube.Live, "jfKfPfyJRdk", "youtu.be/jfKfPfyJRdk")]
    [InlineData("https://www.youtube.com/watch?si=abc&v=jfKfPfyJRdk&t=10", TipoDeCanalDoYouTube.Live, "jfKfPfyJRdk", "youtu.be/jfKfPfyJRdk")]
    [InlineData("https://youtu.be/jfKfPfyJRdk?si=xyz", TipoDeCanalDoYouTube.Live, "jfKfPfyJRdk", "youtu.be/jfKfPfyJRdk")]
    [InlineData("youtube.com/live/jfKfPfyJRdk?feature=share", TipoDeCanalDoYouTube.Live, "jfKfPfyJRdk", "youtu.be/jfKfPfyJRdk")]
    public void Le_OsJeitosDeDigitar(string digitado, TipoDeCanalDoYouTube tipo, string valor, string texto)
    {
        CanalDoYouTube? canal = CanalDoYouTube.Ler(digitado);

        Assert.NotNull(canal);
        Assert.Equal(tipo, canal.Tipo);
        Assert.Equal(valor, canal.Valor);
        Assert.Equal(texto, canal.Texto);
        Assert.Equal(canal, CanalDoYouTube.Ler(canal.Texto)); // o gravado é lido de novo igual
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    [InlineData("ab")]
    [InlineData("não é um canal!")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/watch?v=curto")]
    [InlineData("https://www.youtube.com/channel/naoehid")]
    [InlineData("https://youtu.be/")]
    public void NaoEntende_DevolveNull(string? digitado) => Assert.Null(CanalDoYouTube.Ler(digitado));
}
