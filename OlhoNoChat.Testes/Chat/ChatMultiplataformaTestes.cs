using System.Text.Json;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Twitch;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Testes.Chat;

/// <summary>
/// Chat Multiplataforma ligado: só o Padrão, sem escrever no chat, sem resgates, e o canal do YouTube só é lido com a
/// função ligada.
/// </summary>
public class ChatMultiplataformaTestes
{
    private readonly Opcoes _opcoes = new() { Canal = "hyfronx", ChatMultiplataforma = true, CanalDoYouTube = "@Hyfronx" };

    private LogicaCaixaDeEscrever Caixa() => new(() => _opcoes, () => true, () => true, () => "Hyfronx",
        (_, _) => Task.FromResult(new EnvioDeMensagem.Resultado(EnvioDeMensagem.Situacao.Enviada)));

    [Fact]
    public void SemEscrever_SemBotaoNemCaixa_EOAtalhoAvisa()
    {
        LogicaCaixaDeEscrever caixa = Caixa();

        Assert.False(caixa.BotaoEscreverNaTela);
        Assert.False(caixa.CaixaDoAppDisponivel);
        Assert.Equal(LogicaCaixaDeEscrever.AcaoDoAtalho.AvisarQueNaoDa, caixa.AoApertarOAtalho(false, false, false));
        Assert.Equal(ChatMultiplataforma.TextoSemEscrever, caixa.TextoQueNaoDa);
        Assert.Null(caixa.TextoQueDa(bordasVisiveis: true));

        _opcoes.ChatMultiplataforma = false;
        Assert.True(caixa.BotaoEscreverNaTela);
        Assert.True(caixa.CaixaDoAppDisponivel);
    }

    [Fact]
    public void CaixaDaTwitch_DesligadaMesmoNoChatOficialSalvo()
    {
        _opcoes.TipoDeChat = (int)TipoDeChat.ChatOficial;
        _opcoes.CaixaDaTwitch = true;

        Assert.False(Caixa().UsaCaixaDaTwitch);
        Assert.IsType<ChatPadrao>(PaginaDoChat.DasOpcoes(_opcoes)); // e a página é a do Padrão
    }

    [Fact]
    public void Resgates_DesligadosNoMultiplataforma()
    {
        _opcoes.MostrarResgates = true;
        Assert.False(ChatMultiplataforma.ResgatesLigados(_opcoes));

        _opcoes.ChatMultiplataforma = false;
        Assert.True(ChatMultiplataforma.ResgatesLigados(_opcoes));
    }

    [Fact]
    public void CanalALer_SoComAFuncaoLigada()
    {
        Assert.Equal(CanalDoYouTube.Ler("@Hyfronx"), ChatMultiplataforma.CanalALer(_opcoes));

        _opcoes.ChatMultiplataforma = false;
        Assert.Null(ChatMultiplataforma.CanalALer(_opcoes));
    }

    [Fact]
    public void Corrigir_VoltaParaOPadrao()
    {
        _opcoes.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado;
        _opcoes.CanalDoYouTube = null!;
        _opcoes.Corrigir();

        Assert.Equal((int)TipoDeChat.Padrao, _opcoes.TipoDeChat);
        Assert.Equal(string.Empty, _opcoes.CanalDoYouTube);
    }

    [Fact]
    public void Restaurar_GuardaOCanalDoYouTube_EDesligaAFuncao()
    {
        Opcoes restauradas = _opcoes.RestauradasParaOPadrao();

        Assert.Equal("@Hyfronx", restauradas.CanalDoYouTube);
        Assert.False(restauradas.ChatMultiplataforma);
    }

    [Fact]
    public void Pagina_RecebeAOpcaoDosIcones()
    {
        using JsonDocument ligado = JsonDocument.Parse(ContratoComAPagina.JsonDeConfiguracoes(_opcoes));
        Assert.True(ligado.RootElement.GetProperty("multiplatform").GetBoolean());

        _opcoes.ChatMultiplataforma = false;
        using JsonDocument desligado = JsonDocument.Parse(ContratoComAPagina.JsonDeConfiguracoes(_opcoes));
        Assert.False(desligado.RootElement.GetProperty("multiplatform").GetBoolean());
    }

    [Fact]
    public void Kick_LidaSoComAFuncaoLigada()
    {
        _opcoes.CanalDaKick = "https://kick.com/Gaules";
        Assert.Equal("gaules", ChatMultiplataforma.KickALer(_opcoes));

        _opcoes.ChatMultiplataforma = false;
        Assert.Null(ChatMultiplataforma.KickALer(_opcoes));
    }

    [Fact]
    public void TrocarOCanalDaKick_NaoRecarregaOChat()
    {
        string antes = PaginaDoChat.DasOpcoes(_opcoes).ChaveDeRecarga;
        _opcoes.CanalDaKick = "gaules";

        Assert.Equal(antes, PaginaDoChat.DasOpcoes(_opcoes).ChaveDeRecarga);
    }

    [Fact]
    public void Restaurar_GuardaOCanalDaKick()
    {
        _opcoes.CanalDaKick = "gaules";

        Assert.Equal("gaules", _opcoes.RestauradasParaOPadrao().CanalDaKick);
    }

    [Fact]
    public void LigarOuDesligar_NaoRecarregaOChat()
    {
        string ligado = PaginaDoChat.DasOpcoes(_opcoes).ChaveDeRecarga;
        _opcoes.ChatMultiplataforma = false;
        Assert.Equal(ligado, PaginaDoChat.DasOpcoes(_opcoes).ChaveDeRecarga);
    }

    [Fact]
    public void TikTokALer_SoComAFuncaoLigada()
    {
        _opcoes.CanalDoTikTok = "https://www.tiktok.com/@Fulano/live";
        Assert.Equal("fulano", ChatMultiplataforma.TikTokALer(_opcoes));

        _opcoes.ChatMultiplataforma = false;
        Assert.Null(ChatMultiplataforma.TikTokALer(_opcoes));
    }

    [Fact]
    public void SoOTikTok_AbreOChatSemTwitch()
    {
        _opcoes.Canal = string.Empty;
        _opcoes.CanalDoYouTube = string.Empty;
        _opcoes.CanalDoTikTok = "fulano";

        Assert.True(ChatMultiplataforma.TemOutroCanal(_opcoes));
        Assert.IsType<ChatPadrao>(PaginaDoChat.DasOpcoes(_opcoes));
    }

    [Fact]
    public void TrocarOCanalDoTikTok_NaoRecarregaOChat()
    {
        string antes = PaginaDoChat.DasOpcoes(_opcoes).ChaveDeRecarga;
        _opcoes.CanalDoTikTok = "fulano";

        Assert.Equal(antes, PaginaDoChat.DasOpcoes(_opcoes).ChaveDeRecarga);
    }

    [Fact]
    public void Restaurar_GuardaOCanalDoTikTok_ECorrigirNaoDeixaNulo()
    {
        _opcoes.CanalDoTikTok = "fulano";
        Assert.Equal("fulano", _opcoes.RestauradasParaOPadrao().CanalDoTikTok);

        _opcoes.CanalDoTikTok = null!;
        _opcoes.Corrigir();
        Assert.Equal(string.Empty, _opcoes.CanalDoTikTok);
    }
}
