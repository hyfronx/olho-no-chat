using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OlhoNoChat.Controles;

/// <summary>
/// Extras das caixas de texto, mostrados pelo estilo delas (Estilos/Controles.xaml): o texto de exemplo cinza de uma caixa
/// vazia ("Nome do usuário na Twitch", "#RRGGBB") e o "x" que apaga o que foi digitado (<see cref="BotaoDeApagarProperty"/>).
/// </summary>
public static class CaixaDeTexto
{
    public static readonly DependencyProperty TextoDeExemploProperty = DependencyProperty.RegisterAttached(
        "TextoDeExemplo", typeof(string), typeof(CaixaDeTexto), new FrameworkPropertyMetadata(string.Empty));

    public static string GetTextoDeExemplo(DependencyObject elemento) => (string)elemento.GetValue(TextoDeExemploProperty);

    public static void SetTextoDeExemplo(DependencyObject elemento, string valor) => elemento.SetValue(TextoDeExemploProperty, valor);

    /// <summary>Um "x" no fim da caixa, enquanto ela tem texto, que apaga tudo (e deixa o cursor nela).</summary>
    public static readonly DependencyProperty BotaoDeApagarProperty = DependencyProperty.RegisterAttached(
        "BotaoDeApagar", typeof(bool), typeof(CaixaDeTexto), new FrameworkPropertyMetadata(false));

    public static bool GetBotaoDeApagar(DependencyObject elemento) => (bool)elemento.GetValue(BotaoDeApagarProperty);

    public static void SetBotaoDeApagar(DependencyObject elemento, bool valor) => elemento.SetValue(BotaoDeApagarProperty, valor);

    /// <summary>O que o "x" faz, na caixa de texto em que ele está.</summary>
    public static readonly RoutedUICommand Apagar = new("Apagar", nameof(Apagar), typeof(CaixaDeTexto));

    static CaixaDeTexto()
    {
        CommandManager.RegisterClassCommandBinding(typeof(TextBox), new CommandBinding(Apagar, (sender, _) =>
        {
            var caixa = (TextBox)sender;
            caixa.Clear();
            caixa.Focus();
        }));
    }
}
