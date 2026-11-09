using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Kick;
using OlhoNoChat.TikTok;
using OlhoNoChat.Twitch;
using OlhoNoChat.YouTube;
using static OlhoNoChat.Testes.TikTok.AmostrasDoTikTok;

namespace OlhoNoChat.Testes.Chat;

/// <summary>"Mostrar viewers": o número curto, as respostas de cada plataforma, a contagem, a faixa e a página.</summary>
public class ViewersTestes
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1 mil")]
    [InlineData(1199, "1,1 mil")]
    [InlineData(1999, "1,9 mil")]
    [InlineData(9999, "9,9 mil")]
    [InlineData(12_345, "12 mil")]
    [InlineData(999_999, "999 mil")]
    [InlineData(1_500_000, "1,5 mi")]
    [InlineData(23_000_000, "23 mi")]
    public void Curto_ComoAsPlataformasMostram(long numero, string texto) => Assert.Equal(texto, Viewers.Curto(numero));

    [Fact]
    public void Inteiro_ComPontoDosMilhares() => Assert.Equal("3.135", Viewers.Inteiro(3135));

    [Fact]
    public void Total_SoComMaisDeUmaPlataforma()
    {
        Assert.Null(new Viewers(320, null, null, null).Total);
        Assert.Equal(3455, new Viewers(320, 3135, null, 0).Total);
        Assert.Null(Viewers.Nenhum.Total);
    }

    // --- Respostas ---

    [Fact]
    public void Twitch_AoVivoForaDoArENaoExiste()
    {
        Assert.Equal(320, ViewersDaTwitch.Ler("""{"data":{"user":{"stream":{"viewersCount":320}}},"extensions":{}}"""));
        Assert.Null(ViewersDaTwitch.Ler("""{"data":{"user":{"stream":null}}}"""));
        Assert.Null(ViewersDaTwitch.Ler("""{"data":{"user":null}}"""));
        Assert.Throws<JsonException>(() => ViewersDaTwitch.Ler("""{"errors":[{"message":"x"}]}"""));
    }

    [Fact]
    public void Twitch_PedidoComOCanalNasVariaveis()
    {
        using JsonDocument pedido = JsonDocument.Parse(ViewersDaTwitch.Pedido("gaules"));
        Assert.Equal("gaules", pedido.RootElement.GetProperty("variables").GetProperty("login").GetString());
        Assert.Contains("viewersCount", pedido.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public void Kick_AoVivoEForaDoAr()
    {
        Assert.Equal(1202, SalaDaKick.LerViewers("""{"id":1,"livestream":{"is_live":true,"viewer_count":1202}}"""));
        Assert.Null(SalaDaKick.LerViewers("""{"id":1,"livestream":null}"""));
    }

    // Como o updated_metadata respondeu em 2026-11-08 (só o que importa)
    private const string MetadadosDoYouTube = """
        {"actions":[{"updateViewershipAction":{"viewCount":{"videoViewCountRenderer":{"viewCount":{"simpleText":"3,135 watching now"},
        "isLive":true,"extraShortViewCount":{"simpleText":"3.1K"},"unlabeledViewCountValue":{"simpleText":"3,135"},"originalViewCount":"3135"}}}},
        {"updateDateTextAction":{"dateText":{"simpleText":"Started streaming 3 hours ago"}}}]}
        """;

    [Fact]
    public void YouTube_ONumeroExato()
    {
        Assert.Equal(3135, ViewersDoYouTube.Ler(MetadadosDoYouTube));
        Assert.Null(ViewersDoYouTube.Ler(MetadadosDoYouTube.Replace("\"isLive\":true", "\"isLive\":false")));
        Assert.Null(ViewersDoYouTube.Ler("""{"responseContext":{}}"""));
    }

    [Fact]
    public void YouTube_PedidoComALiveEAVersao()
    {
        using JsonDocument pedido = JsonDocument.Parse(ViewersDoYouTube.Pedido(new PaginaDaLive("abc123", "chave", "2.2026", "c")));
        Assert.Equal("abc123", pedido.RootElement.GetProperty("videoId").GetString());
        Assert.Equal("2.2026", pedido.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientVersion").GetString());
    }

    [Fact]
    public void TikTok_ONumeroDaSalaENaConexao()
    {
        string sala = Sala(aoVivo: true).Replace("\"status\":2", "\"status\":2,\"liveRoomStats\":{\"userCount\":845}");
        Assert.Equal(845, SalaDoTikTok.Ler(sala).Viewers);
        Assert.Null(SalaDoTikTok.Ler(Sala(aoVivo: true)).Viewers);

        var contagem = new Bruta("WebcastRoomUserSeqMessage", new ProtobufNovo().Numero(3, 512).Numero(7, 9000).ParaBytes());
        Assert.Equal(512, QuadrosDoTikTok.Ler(Quadro(1, contagem), IdDoDono, comHistorico: false).Viewers);
        Assert.Null(QuadrosDoTikTok.Ler(Quadro(1, MensagemDoChat(1, "viewer1", "oi")), IdDoDono, comHistorico: false).Viewers);
    }

    // --- Contagem ---

    private sealed class FontesFalsas : IFontesDeViewers
    {
        public long? Twitch = 320;
        public long? YouTube = 3135;
        public long? Kick = 1202;
        public bool TwitchFalha;
        public List<string> Pedidos { get; } = [];

        public Task<long?> TwitchAsync(string canal, CancellationToken cancelar)
        {
            lock (Pedidos)
                Pedidos.Add("twitch:" + canal);
            return TwitchFalha ? Task.FromException<long?>(new HttpRequestException("caiu")) : Task.FromResult(Twitch);
        }

        public Task<long?> YouTubeAsync(PaginaDaLive live, CancellationToken cancelar)
        {
            lock (Pedidos)
                Pedidos.Add("youtube:" + live.IdDaLive);
            return Task.FromResult(YouTube);
        }

        public Task<long?> KickAsync(string canal, CancellationToken cancelar)
        {
            lock (Pedidos)
                Pedidos.Add("kick:" + canal);
            return Task.FromResult(Kick);
        }
    }

    private static readonly PaginaDaLive Live = new("abc123", "chave", "2.2026", "c");

    private static async Task<Viewers> Esperar(ContagemDeViewers contagem, Func<Viewers, bool> condicao)
    {
        for (int i = 0; i < 200 && !condicao(contagem.Atual); i++)
            await Task.Delay(10);
        return contagem.Atual;
    }

    [Fact]
    public async Task Contagem_PerguntaCadaPlataformaEJuntaOTikTok()
    {
        var fontes = new FontesFalsas();
        using var contagem = new ContagemDeViewers(NullLogger.Instance, fontes, () => Live, TimeSpan.FromHours(1));
        int avisos = 0;
        contagem.Mudou += _ => Interlocked.Increment(ref avisos);

        contagem.Contar("hyfronx", youTube: true, kick: "gaules", tikTok: true);
        contagem.TikTok(512);

        Viewers viewers = await Esperar(contagem, v => v.Twitch != null && v.TikTok != null);
        Assert.Equal(new Viewers(320, 3135, 1202, 512), viewers);
        Assert.Equal(5169, viewers.Total);
        Assert.Contains("twitch:hyfronx", fontes.Pedidos);
        Assert.Contains("youtube:abc123", fontes.Pedidos);
        Assert.Contains("kick:gaules", fontes.Pedidos);
        Assert.True(avisos >= 1);
    }

    [Fact]
    public async Task Contagem_OQueDeixouDeSerContadoSomeNaHoraEDesligarLimpaTudo()
    {
        var fontes = new FontesFalsas();
        using var contagem = new ContagemDeViewers(NullLogger.Instance, fontes, () => null, TimeSpan.FromHours(1));
        contagem.Contar("hyfronx", youTube: false, kick: "gaules", tikTok: false);
        await Esperar(contagem, v => v.Kick != null);

        // TikTok sem estar sendo contado: ignorado
        contagem.TikTok(99);
        Assert.Null(contagem.Atual.TikTok);

        contagem.Contar("hyfronx", youTube: false, kick: null, tikTok: false);
        Assert.Null(contagem.Atual.Kick);
        Assert.Equal(320, contagem.Atual.Twitch);

        contagem.Contar(null, false, null, false);
        Assert.Equal(Viewers.Nenhum, contagem.Atual);
    }

    [Fact]
    public async Task Contagem_PlataformaQueFalhouFicaSemNumero()
    {
        var fontes = new FontesFalsas { TwitchFalha = true };
        using var contagem = new ContagemDeViewers(NullLogger.Instance, fontes, () => null, TimeSpan.FromHours(1));
        contagem.Contar("hyfronx", false, "gaules", false);

        Viewers viewers = await Esperar(contagem, v => v.Kick != null);
        Assert.Null(viewers.Twitch);
        Assert.Equal(1202, viewers.Kick);
    }

    [Fact]
    public async Task Contagem_PerguntaDeNovoACadaIntervalo()
    {
        var fontes = new FontesFalsas();
        using var contagem = new ContagemDeViewers(NullLogger.Instance, fontes, () => null, TimeSpan.FromMilliseconds(30));
        contagem.Contar("hyfronx", false, null, false);
        await Esperar(contagem, v => v.Twitch == 320);

        fontes.Twitch = 400;
        Assert.Equal(400, (await Esperar(contagem, v => v.Twitch == 400)).Twitch);
    }

    // --- Faixa e página ---

    private static LogicaFaixaDoCanal Faixa(Opcoes opcoes) =>
        new(() => opcoes, () => false, _ => Task.FromResult<bool?>(true), (_, _, _, _) => { });

    [Fact]
    public void Faixa_SoAsPlataformasDaFaixaEASoma()
    {
        var opcoes = new Opcoes { Canal = "hyfronx", ChatMultiplataforma = true, CanalDaKick = "gaules" };
        var faixa = Faixa(opcoes);

        faixa.Viewers = new Viewers(320, 3135, 1202, null);

        Assert.True(faixa.TemViewersNaFaixa);
        Assert.Equal("320", faixa.ViewersDaTwitch);
        Assert.False(faixa.NomeDaTwitchNaTela);
        Assert.Equal("", faixa.ViewersDoYouTube); // sem canal do YouTube na faixa
        Assert.Equal("1,2 mil", faixa.ViewersDaKick);
        Assert.Equal("1,5 mil", faixa.ViewersNoTotal);

        opcoes.ChatMultiplataforma = false;
        faixa.Atualizar(faixaNaTela: true);
        Assert.Equal("", faixa.ViewersDaKick);
        Assert.Equal("", faixa.ViewersNoTotal);
    }

    [Fact]
    public void Faixa_ViewersPorMaisTempoENomesSoPorUmInstante()
    {
        var opcoes = new Opcoes { Canal = "hyfronx", ChatMultiplataforma = true, CanalDaKick = "gaules" };
        var faixa = Faixa(opcoes);
        faixa.Viewers = new Viewers(320, null, null, null); // a Kick fora do ar

        Assert.Equal(LogicaFaixaDoCanal.TempoDosNomes, faixa.Alternar());
        Assert.True(faixa.MostrandoNomes);
        Assert.Equal("", faixa.ViewersDaTwitch);
        Assert.True(faixa.NomeDaTwitchNaTela);

        Assert.Equal(LogicaFaixaDoCanal.TempoDosViewers, faixa.Alternar());
        Assert.False(faixa.NomeDaTwitchNaTela);
        // Sem número, o canal mostra sempre o nome
        Assert.True(faixa.NomeDaKickNaTela);
        Assert.True(LogicaFaixaDoCanal.TempoDosViewers > LogicaFaixaDoCanal.TempoDosNomes);
    }

    [Fact]
    public void Faixa_SemViewersMostraOsNomes()
    {
        var faixa = Faixa(new Opcoes { Canal = "hyfronx" });

        Assert.False(faixa.TemViewersNaFaixa);
        Assert.True(faixa.NomeDaTwitchNaTela);
    }

    [Fact]
    public void Pagina_LinhaComOsNumerosOuNada()
    {
        string script = ContratoComAPagina.MostrarViewers(new Viewers(320, null, 1202, null));
        Assert.Contains("""{"items":[{"platform":"twitch","text":"320"},{"platform":"kick","text":"1,2 mil"}],"total":"1,5 mil"}""", script);
        // Uma plataforma só (só Twitch): sem o Total
        Assert.Contains("""{"items":[{"platform":"twitch","text":"320"}],"total":null}""",
            ContratoComAPagina.MostrarViewers(new Viewers(320, null, null, null)));
        Assert.Contains("oncChat.viewers(null)", ContratoComAPagina.MostrarViewers(null));
        Assert.Contains("oncChat.viewers(null)", ContratoComAPagina.MostrarViewers(Viewers.Nenhum));
    }
}
