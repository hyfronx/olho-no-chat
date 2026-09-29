using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba Aparência: tema e CSS do tema "Nenhum" (Padrão), "Texto das mensagens" (Padrão e chat oficial), a aparência
/// do chat oficial e o CSS do endereço personalizado. Cada CSS é uma opção separada.
/// </summary>
public sealed partial class LogicaAparencia : LogicaDaPagina
{
    /// <summary>O que o editor do endereço personalizado mostra quando nada foi guardado para ele.</summary>
    public const string ExemploDoCssDoEndereco = "body { background-color: rgba(0, 0, 0, 0); margin: 0px auto; overflow: hidden; }";

    public static IReadOnlyList<OpcaoDaLista> Temas { get; } =
    [
        // A posição é o número gravado (Opcoes.Tema)
        new("Nenhum (CSS personalizado)", Opcoes.TemaNenhum.ToString()),
        new("Padrão", Opcoes.TemaPadrao.ToString()),
    ];

    public static IReadOnlyList<OpcaoDaLista> Cores { get; } =
    [
        new("Padrão do tema", ""),
        new("Branco", "#FFFFFF", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        new("Amarelo claro", "#FFF3A6", Color.FromRgb(0xFF, 0xF3, 0xA6)),
        new("Verde claro", "#B8F5B0", Color.FromRgb(0xB8, 0xF5, 0xB0)),
        new("Azul claro", "#A8DCFF", Color.FromRgb(0xA8, 0xDC, 0xFF)),
        new("Rosa claro", "#FFB8E0", Color.FromRgb(0xFF, 0xB8, 0xE0)),
        new("Laranja claro", "#FFC9A0", Color.FromRgb(0xFF, 0xC9, 0xA0)),
        new("Cinza claro", "#D0D0D0", Color.FromRgb(0xD0, 0xD0, 0xD0)),
    ];

    public static IReadOnlyList<OpcaoDaLista> Contornos { get; } =
    [
        new("Contorno preto", "theme"),
        new("Sombra suave", "soft"),
        new("Nenhum", "none"),
    ];

    public static IReadOnlyList<OpcaoDaLista> Fontes { get; } =
    [
        new("Padrão do tema", "theme"),
        new("Segoe UI (a do Windows)", "Segoe UI", Fonte: "Segoe UI"),
        new("Arial", "Arial", Fonte: "Arial"),
        new("Verdana", "Verdana", Fonte: "Verdana"),
    ];

    // Tema (Padrão)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarCssDoTemaNenhum))]
    private string _tema = Opcoes.TemaPadrao.ToString();

    [ObservableProperty]
    private string _cssDoTemaNenhum = string.Empty;

    // Texto das mensagens (Padrão e chat oficial)
    [ObservableProperty]
    private string _corDoTexto = string.Empty;

    [ObservableProperty]
    private string _contornoDasLetras = string.Empty;

    [ObservableProperty]
    private string _fonte = string.Empty;

    [ObservableProperty]
    private bool _mostrarHorario;

    // Aparência (CSS) do chat oficial: com a aparência padrão, o editor mostra o CSS do app só para ler
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoEditorDoChatOficial))]
    private bool _aparenciaPadraoNoChatOficial;

    /// <summary>O CSS próprio do chat oficial (o que é gravado com a aparência padrão desligada).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoEditorDoChatOficial))]
    private string _cssDoChatOficial = string.Empty;

    // Endereço personalizado
    [ObservableProperty]
    private string _cssDoEnderecoPersonalizado = string.Empty;

    /// <summary>A caixa do CSS aparece só com o tema "Nenhum".</summary>
    public bool MostrarCssDoTemaNenhum => Tema == Opcoes.TemaNenhum.ToString();

    /// <summary>
    /// O editor do chat oficial: o CSS padrão do app (somente leitura) ou o CSS próprio. Desligar e ligar de novo a
    /// aparência padrão não perde o que foi digitado.
    /// </summary>
    public string TextoDoEditorDoChatOficial
    {
        get => AparenciaPadraoNoChatOficial ? CssDoChat.PadraoDoChatOficial : CssDoChatOficial;
        set
        {
            if (!AparenciaPadraoNoChatOficial)
                CssDoChatOficial = value;
        }
    }

    public override void Carregar(Opcoes opcoes)
    {
        Tema = (opcoes.Tema == Opcoes.TemaNenhum ? Opcoes.TemaNenhum : Opcoes.TemaPadrao).ToString();
        CssDoTemaNenhum = opcoes.CssDoTemaNenhum.Length > 0 ? opcoes.CssDoTemaNenhum : CssDoChat.ExemploDoTemaNenhum;

        // Um valor que não está na lista aparece como o primeiro item ("Padrão do tema", "Contorno preto")
        CorDoTexto = NaLista(Cores, opcoes.CorDoTexto);
        ContornoDasLetras = NaLista(Contornos, opcoes.ContornoDasLetras);
        Fonte = NaLista(Fontes, opcoes.Fonte);
        MostrarHorario = opcoes.MostrarHorario;

        AparenciaPadraoNoChatOficial = opcoes.AparenciaPadraoNoChatOficial;
        // Nada guardado ainda: o editor começa com o CSS padrão, para editar a partir dele (decisão 11)
        CssDoChatOficial = opcoes.CssDoChatOficial.Length > 0 ? opcoes.CssDoChatOficial : CssDoChat.PadraoDoChatOficial;

        // Vazio e com o endereço salvo, fica vazio (salvar sem mexer não grava o exemplo)
        CssDoEnderecoPersonalizado = opcoes.TipoDeChat == (int)TipoDeChat.EnderecoPersonalizado || opcoes.CssDoEnderecoPersonalizado.Length > 0
            ? opcoes.CssDoEnderecoPersonalizado
            : ExemploDoCssDoEndereco;
    }

    private static string NaLista(IReadOnlyList<OpcaoDaLista> lista, string? valor) =>
        lista.FirstOrDefault(o => o.Valor == valor)?.Valor ?? lista[0].Valor;

    public override void Gravar(Opcoes opcoes)
    {
        switch (TipoNaTela)
        {
            case TipoDeChat.Padrao:
                GravarTextoDasMensagens(opcoes);
                opcoes.Tema = int.Parse(Tema);
                if (opcoes.Tema == Opcoes.TemaNenhum)
                    opcoes.CssDoTemaNenhum = CssDoTemaNenhum;
                break;
            case TipoDeChat.ChatOficial:
                GravarTextoDasMensagens(opcoes);
                opcoes.AparenciaPadraoNoChatOficial = AparenciaPadraoNoChatOficial;
                if (!AparenciaPadraoNoChatOficial)
                    opcoes.CssDoChatOficial = CssDoChatOficial;
                break;
            case TipoDeChat.EnderecoPersonalizado:
                opcoes.CssDoEnderecoPersonalizado = string.IsNullOrWhiteSpace(CssDoEnderecoPersonalizado)
                                                    || CssDoEnderecoPersonalizado.Equals("css", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : CssDoEnderecoPersonalizado;
                break;
        }
    }

    private void GravarTextoDasMensagens(Opcoes opcoes)
    {
        opcoes.CorDoTexto = CorDoTexto;
        opcoes.ContornoDasLetras = ContornoDasLetras;
        opcoes.Fonte = Fonte;
        opcoes.MostrarHorario = MostrarHorario;
    }

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Aparencia.Tema"] = Tema;
        estado["Aparencia.CssDoTemaNenhum"] = CssDoTemaNenhum;
        estado["Aparencia.CorDoTexto"] = CorDoTexto;
        estado["Aparencia.ContornoDasLetras"] = ContornoDasLetras;
        estado["Aparencia.Fonte"] = Fonte;
        estado["Aparencia.MostrarHorario"] = MostrarHorario.ToString();
        estado["Aparencia.AparenciaPadraoNoChatOficial"] = AparenciaPadraoNoChatOficial.ToString();
        estado["Aparencia.CssDoChatOficial"] = CssDoChatOficial;
        estado["Aparencia.CssDoEnderecoPersonalizado"] = CssDoEnderecoPersonalizado;
    }
}
