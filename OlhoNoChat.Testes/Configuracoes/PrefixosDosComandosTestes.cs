using System.Text.Json;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;

namespace OlhoNoChat.Testes.Configuracoes;

/// <summary>
/// Os "Prefixos dos comandos" da aba Chat, uma lista só para o chat e o overlay: a caixa aceita espaço, vírgula ou ponto
/// e vírgula, mostra separados por espaço, vazia fica com os salvos, e um arquivo editado à mão volta para um que vale.
/// </summary>
public sealed class PrefixosDosComandosTestes
{
    private static Opcoes Corrigidas(string json)
    {
        Opcoes opcoes = JsonSerializer.Deserialize<Opcoes>(json, ArquivoDeConfiguracoes.Formato)!;
        opcoes.Corrigir();
        return opcoes;
    }

    private static LogicaChat Aba(params string[] salvos)
    {
        var chat = new LogicaChat(() => new Opcoes(), () => true);
        chat.Carregar(new Opcoes { PrefixosDosComandos = [.. salvos] });
        return chat;
    }

    [Fact]
    public void PrimeiraInstalacao_SoOPontoDeExclamacao()
    {
        Assert.Equal(["!"], new Opcoes().PrefixosDosComandos);
    }

    [Theory]
    [InlineData("! $", new[] { "!", "$" })]
    [InlineData("!,$", new[] { "!", "$" })]
    [InlineData(" !,  $ ; ? ", new[] { "!", "$", "?" })]
    [InlineData("!\n$\t?", new[] { "!", "$", "?" })]
    [InlineData("! ! $ !", new[] { "!", "$" })]
    [InlineData("!! !", new[] { "!!", "!" })]
    [InlineData("", new string[0])]
    [InlineData(" , ; ", new string[0])]
    public void LerPrefixos_EspacoVirgulaOuPontoEVirgulaSemRepetir(string texto, string[] prefixos)
    {
        Assert.Equal(prefixos, Opcoes.LerPrefixos(texto));
    }

    [Fact]
    public void LerPrefixos_NullEhNenhum()
    {
        Assert.Empty(Opcoes.LerPrefixos(null));
    }

    [Fact]
    public void TextoDosPrefixos_SeparadosPorEspaco()
    {
        Assert.Equal("! $ ?", Opcoes.TextoDosPrefixos(["!", "$", "?"]));
    }

    [Theory]
    [InlineData("""{"PrefixosDosComandos":null}""")]
    [InlineData("""{"PrefixosDosComandos":[]}""")]
    [InlineData("""{"PrefixosDosComandos":["  ",null,""]}""")]
    public void Corrigir_ListaSemPrefixoVoltaParaOPadrao(string json)
    {
        Assert.Equal(["!"], Corrigidas(json).PrefixosDosComandos);
    }

    [Fact]
    public void Corrigir_ArquivoEditadoAMaoViraUmPrefixoPorItem()
    {
        Assert.Equal(["!", "$", "?"], Corrigidas("""{"PrefixosDosComandos":[" ! ","! $","?",null]}""").PrefixosDosComandos);
    }

    [Fact]
    public void Corrigir_OPrefixoAntigoDoOverlaySoEhIgnorado()
    {
        // Só existiu na 2.9.0 antes de sair (nunca publicada): o chat e o overlay ficam com a lista da aba Chat
        Opcoes o = Corrigidas("""{"PrefixoDosComandosNoOverlay":"?","PrefixosDosComandos":["$"]}""");

        Assert.Equal(["$"], o.PrefixosDosComandos);
    }

    [Fact]
    public void Aba_MostraOsSalvosSeparadosPorEspaco()
    {
        LogicaChat chat = Aba("!", "$");

        Assert.Equal("! $", chat.PrefixosDosComandos);
        Assert.Equal(["!", "$"], chat.PrefixosQueValem);
    }

    [Fact]
    public void Aba_GravaComoListaENaTelaFicaSeparadoPorEspaco()
    {
        LogicaChat chat = Aba("!");
        chat.PrefixosDosComandos = "!, $,?";
        var opcoes = new Opcoes();

        chat.Gravar(opcoes);

        Assert.Equal(["!", "$", "?"], opcoes.PrefixosDosComandos);
        Assert.Equal("! $ ?", chat.PrefixosDosComandos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ; ")]
    public void Aba_CaixaVaziaFicaComOsSalvos(string vazia)
    {
        LogicaChat chat = Aba("$", "?");
        chat.PrefixosDosComandos = vazia;
        Assert.Equal(["$", "?"], chat.PrefixosQueValem);
        var opcoes = new Opcoes { PrefixosDosComandos = ["$", "?"] };

        chat.Gravar(opcoes);

        Assert.Equal(["$", "?"], opcoes.PrefixosDosComandos);
        Assert.Equal("$ ?", chat.PrefixosDosComandos); // a tela mostra o que ficou valendo
    }

    [Fact]
    public void Aba_OsPrefixosSoSaoGravadosComOPadrao()
    {
        // Os filtros do chat só existem no Padrão (o overlay também só funciona com ele)
        LogicaChat chat = Aba("!");
        chat.TipoNaTela = TipoDeChat.ChatOficial;
        chat.PrefixosDosComandos = "$";
        var opcoes = new Opcoes();

        chat.Gravar(opcoes);

        Assert.Equal(["!"], opcoes.PrefixosDosComandos);
    }

    [Fact]
    public void Aba_EstadoIgualComVirgulaOuEspaco()
    {
        LogicaChat chat = Aba("!", "$");
        var antes = new Dictionary<string, string>();
        chat.Estado(antes);

        chat.PrefixosDosComandos = " !,$ ";
        var depois = new Dictionary<string, string>();
        chat.Estado(depois);

        Assert.Equal(antes, depois);
    }

    [Theory]
    [InlineData(new[] { "!" }, "Esconde as mensagens que começam com !, como !discord. Os prefixos ficam na aba Chat.")]
    [InlineData(new[] { "!", "$" }, "Esconde as mensagens que começam com ! ou $, como !discord. Os prefixos ficam na aba Chat.")]
    [InlineData(new[] { "$", "!", "?" }, "Esconde as mensagens que começam com $, ! ou ?, como $discord. Os prefixos ficam na aba Chat.")]
    [InlineData(new string[0], "Esconde as mensagens que começam com !, como !discord. Os prefixos ficam na aba Chat.")]
    public void DicaDoOverlay_CitaOsPrefixosDaAbaChat(string[] prefixos, string dica)
    {
        var overlay = new LogicaOverlay { PrefixosDoChat = prefixos };

        Assert.Equal(dica, overlay.DicaDosComandos);
    }
}
