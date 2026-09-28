#nullable enable
using System.Windows;

namespace OlhoNoChat.Controles;

/// <summary>
/// O texto de exemplo cinza de uma caixa de texto vazia ("Nome do usuário na Twitch", "#RRGGBB"). O estilo das
/// caixas (Estilos/Controles.xaml) mostra o texto enquanto a caixa está vazia.
/// </summary>
public static class CaixaDeTexto
{
    public static readonly DependencyProperty TextoDeExemploProperty = DependencyProperty.RegisterAttached(
        "TextoDeExemplo", typeof(string), typeof(CaixaDeTexto), new FrameworkPropertyMetadata(string.Empty));

    public static string GetTextoDeExemplo(DependencyObject elemento) => (string)elemento.GetValue(TextoDeExemploProperty);

    public static void SetTextoDeExemplo(DependencyObject elemento, string valor) => elemento.SetValue(TextoDeExemploProperty, valor);
}
