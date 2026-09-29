using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OlhoNoChat.Controles;

/// <summary>
/// Mostra um elemento quando o valor ligado a ele é verdadeiro ou um texto não vazio (e o esconde com falso, null ou
/// ""). Com o parâmetro "Nao", o contrário.
/// </summary>
public sealed class VisivelSe : IValueConverter
{
    public object Convert(object? valor, Type tipo, object? parametro, CultureInfo cultura)
    {
        bool mostrar = valor switch
        {
            bool b => b,
            string s => s.Length > 0,
            null => false,
            _ => true,
        };
        if (parametro is "Nao")
            mostrar = !mostrar;
        return mostrar ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? valor, Type tipo, object? parametro, CultureInfo cultura) => throw new NotSupportedException();
}
