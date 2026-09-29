using System.Windows.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Janelas.Chat;

namespace OlhoNoChat.Testes.Janelas;

public class AvisosNoChatTestes
{
    private bool _pronta;
    private readonly List<string> _scripts = [];
    private static readonly Atalho CtrlAltF9 = new(Key.F9, ModifierKeys.Control | ModifierKeys.Alt);

    private AvisosNoChat Novos(string? avisoDasConfiguracoes = null) => new(() => _pronta, _scripts.Add, avisoDasConfiguracoes);

    // O texto que um script de mostrar põe na página
    private List<string> Mostrados() => _scripts.Where(s => s.Contains("onc-toast") && s.Contains("createElement"))
        .Select(s => System.Text.Json.JsonSerializer.Deserialize<string>(s[(s.LastIndexOf("})(", StringComparison.Ordinal) + 3)..^2])!)
        .ToList();

    [Fact]
    public void TextoDasBordasOcultas_ComESemAtalho()
    {
        Assert.Equal("Bordas ocultas: agora só o chat fica por cima do jogo.\nPara mostrar de novo: aperte Ctrl + Alt + F9, ou clique " +
                     "com o botão direito no ícone do Olho no Chat perto do relógio.", AvisosNoChat.TextoBordasOcultas(CtrlAltF9));
        Assert.Equal("Bordas ocultas: agora só o chat fica por cima do jogo.\nPara mostrar de novo: clique com o botão direito no " +
                     "ícone do Olho no Chat perto do relógio.", AvisosNoChat.TextoBordasOcultas(null));
    }

    [Fact]
    public void BordasOcultas_EsperaAPaginaCarregar()
    {
        var avisos = Novos();
        avisos.PedirBordasOcultas();
        avisos.MostrarOsQueEsperam(bordasOcultas: true, configuracoesAbertas: false, CtrlAltF9, () => null);
        Assert.Empty(Mostrados());

        _pronta = true;
        avisos.MostrarOsQueEsperam(true, false, CtrlAltF9, () => null);
        Assert.Equal([AvisosNoChat.TextoBordasOcultas(CtrlAltF9)], Mostrados());

        // Uma vez só
        avisos.MostrarOsQueEsperam(true, false, CtrlAltF9, () => null);
        Assert.Single(Mostrados());
    }

    [Fact]
    public void BordasVoltaram_OAvisoSomeEOQueEsperavaEEsquecido()
    {
        var avisos = Novos();
        avisos.PedirBordasOcultas();
        avisos.EsconderBordasOcultas();
        Assert.Contains(AvisosNoChat.ScriptParaEsconder, _scripts);

        _pronta = true;
        avisos.MostrarOsQueEsperam(bordasOcultas: true, false, CtrlAltF9, () => null);
        Assert.Empty(Mostrados());
    }

    [Fact]
    public void DaParaEscrever_EsperaAsConfiguracoesFecharem()
    {
        _pronta = true;
        var avisos = Novos();
        avisos.PedirDaParaEscrever();

        avisos.MostrarOsQueEsperam(false, configuracoesAbertas: true, CtrlAltF9, () => "pode escrever");
        Assert.Empty(Mostrados());

        avisos.MostrarOsQueEsperam(false, configuracoesAbertas: false, CtrlAltF9, () => "pode escrever");
        Assert.Equal(["pode escrever"], Mostrados());
    }

    [Fact]
    public void AvisoDasConfiguracoes_UmaVezEPorUltimo()
    {
        _pronta = true;
        var avisos = Novos("arquivo ilegível");
        avisos.PedirBordasOcultas();

        avisos.MostrarOsQueEsperam(true, false, CtrlAltF9, () => null);
        avisos.MostrarOsQueEsperam(true, false, CtrlAltF9, () => null);

        Assert.Equal([AvisosNoChat.TextoBordasOcultas(CtrlAltF9), "arquivo ilegível"], Mostrados());
    }

    [Fact]
    public void Mostrar_ComAspasEQuebrasDeLinha_VaiComoTextoJson()
    {
        _pronta = true;
        Novos().Mostrar("escolha \"Padrão\"\nlinha 2");

        Assert.Equal(["escolha \"Padrão\"\nlinha 2"], Mostrados());
    }

    [Fact]
    public void GrupoDeEmotes_FiltraSemDiferenciarMaiusculasETiraOsVazios()
    {
        IReadOnlyList<GrupoDeEmotes> grupos =
        [
            new("hyfronx (este canal)", [Emote("1", "hyfLol"), Emote("2", "hyfOi")]),
            new("Globais da Twitch", [Emote("3", "Kappa"), Emote("4", "LUL")]),
        ];

        Assert.Same(grupos, GrupoDeEmotes.Filtrar(grupos, "  "));
        IReadOnlyList<GrupoDeEmotes> achados = GrupoDeEmotes.Filtrar(grupos, "LOL");
        Assert.Single(achados);
        Assert.Equal(["hyfLol"], achados[0].Emotes.Select(e => e.Nome));
        Assert.Empty(GrupoDeEmotes.Filtrar(grupos, "nada"));
    }

    private static EmoteNaLista Emote(string id, string nome) => new(id, nome, animado: false, "1.0", 0, imagens: null);

    [Fact]
    public void GrupoDeEmotes_EmLinhasPoeOTituloEOsEmotesNoMaximoNPorLinha()
    {
        IReadOnlyList<GrupoDeEmotes> grupos =
        [
            new("A", [Emote("1", "a1"), Emote("2", "a2"), Emote("3", "a3"), Emote("4", "a4"), Emote("5", "a5")]),
            new("B", [Emote("6", "b1")]),
        ];

        IReadOnlyList<object> linhas = GrupoDeEmotes.EmLinhas(grupos, 2);

        Assert.Equal(6, linhas.Count);
        Assert.Equal(new TituloDoGrupo("A", Primeiro: true), linhas[0]);
        Assert.Equal(["a1", "a2"], Assert.IsType<LinhaDeEmotes>(linhas[1]).Emotes.Select(e => e.Nome));
        Assert.Equal(["a5"], Assert.IsType<LinhaDeEmotes>(linhas[3]).Emotes.Select(e => e.Nome));
        Assert.Equal(new TituloDoGrupo("B", Primeiro: false), linhas[4]);
        Assert.Single(Assert.IsType<LinhaDeEmotes>(linhas[5]).Emotes);
        // Largura menor que um emote: um por linha
        Assert.Equal(8, GrupoDeEmotes.EmLinhas(grupos, 0).Count);
    }
}
