using System.Text.Json;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Testes.YouTube;

public class PaginaDaLiveTestes
{
    [Fact]
    public void ComLive_IdChaveVersaoEContinuacaoDoChat()
    {
        PaginaDaLive pagina = PaginaDaLive.Ler(AmostrasDoYouTube.PaginaComLive("0ofMyAOEARpeQ2lr%3D"))!;

        Assert.Equal("jfKfPfyJRdk", pagina.IdDaLive);
        Assert.Equal("chave1", pagina.Chave);
        Assert.Equal(AmostrasDoYouTube.Versao, pagina.VersaoDoCliente);
        Assert.Equal("0ofMyAOEARpeQ2lr%3D", pagina.Continuacao); // a do chat, não a "outra" que vem antes
    }

    [Fact]
    public void PaginaDoCanal_SemLive() => Assert.Null(PaginaDaLive.Ler(AmostrasDoYouTube.PaginaSemLive));

    [Fact]
    public void LiveQueJaAcabou_SemLive() =>
        Assert.Null(PaginaDaLive.Ler(AmostrasDoYouTube.PaginaComLive(extra: ""","isReplay":true""")));

    [Fact]
    public void VideoSemChat_SemLive() =>
        Assert.Null(PaginaDaLive.Ler(AmostrasDoYouTube.PaginaComLive().Replace("liveChatRenderer", "outroRenderer")));

    [Fact]
    public void LiveSemAChave_Erro() =>
        Assert.Throws<FormatException>(() => PaginaDaLive.Ler(AmostrasDoYouTube.PaginaComLive().Replace("INNERTUBE_API_KEY", "X")));

    [Theory]
    [InlineData("@Hyfronx", "/@Hyfronx/live")]
    [InlineData("UCSJ4gkVC6NrvII8umztf0Ow", "/channel/UCSJ4gkVC6NrvII8umztf0Ow/live")]
    [InlineData("youtu.be/jfKfPfyJRdk", "/watch?v=jfKfPfyJRdk")]
    public void Caminho_DosTresJeitos(string canal, string caminho) =>
        Assert.Equal(caminho, PaginaDaLive.Caminho(CanalDoYouTube.Ler(canal)!));

    [Fact]
    public void PedidoDoChat_ClienteWebEContinuacao()
    {
        PaginaDaLive pagina = PaginaDaLive.Ler(AmostrasDoYouTube.PaginaComLive())!;
        using JsonDocument pedido = JsonDocument.Parse(pagina.PedidoDoChat("cont1"));

        JsonElement cliente = pedido.RootElement.GetProperty("context").GetProperty("client");
        Assert.Equal("WEB", cliente.GetProperty("clientName").GetString());
        Assert.Equal(AmostrasDoYouTube.Versao, cliente.GetProperty("clientVersion").GetString());
        Assert.Equal("cont1", pedido.RootElement.GetProperty("continuation").GetString());
    }
}
