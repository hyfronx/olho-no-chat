using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace OlhoNoChat.Controles;

/// <summary>
/// O editor de CSS da aba Aparência: fundo claro, fonte Consolas, cores de CSS e sem quebra de linha. O texto pode ser
/// ligado à lógica da página por <see cref="Texto"/> (o <c>Text</c> do editor não aceita ligação). O visual está em
/// Estilos/Controles.xaml.
/// </summary>
public class EditorDeCss : TextEditor
{
    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(EditorDeCss),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (objeto, e) => ((EditorDeCss)objeto).MostrarTexto((string?)e.NewValue ?? string.Empty)));

    private bool _mudandoOTexto;

    public EditorDeCss()
    {
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("CSS");
        TextChanged += (_, _) =>
        {
            if (!_mudandoOTexto)
                Texto = Text;
        };
    }

    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }

    private void MostrarTexto(string texto)
    {
        if (Text == texto)
            return;
        _mudandoOTexto = true;
        try
        {
            Text = texto;
        }
        finally
        {
            _mudandoOTexto = false;
        }
    }
}
