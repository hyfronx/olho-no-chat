using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.Controles;

/// <summary>
/// Os canais da faixa lado a lado, numa linha só. Quando não cabem, o espaço é dividido com justiça: os nomes curtos ficam
/// inteiros e só os compridos são cortados, cada um o mínimo possível (ver <see cref="Larguras"/>). Os escondidos
/// (Collapsed) não contam.
/// </summary>
public class PainelDosCanais : Panel
{
    public static readonly DependencyProperty EspacoProperty = DependencyProperty.Register(
        nameof(Espaco), typeof(double), typeof(PainelDosCanais),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>O espaço entre um canal e outro.</summary>
    public double Espaco
    {
        get => (double)GetValue(EspacoProperty);
        set => SetValue(EspacoProperty, value);
    }

    private List<UIElement> Visiveis => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    protected override Size MeasureOverride(Size disponivel)
    {
        List<UIElement> filhos = Visiveis;
        double altura = 0;
        foreach (UIElement filho in filhos)
            filho.Measure(new Size(double.PositiveInfinity, disponivel.Height));

        double espacos = Espaco * Math.Max(0, filhos.Count - 1);
        double[] larguras = Larguras(filhos.Select(f => f.DesiredSize.Width).ToArray(), disponivel.Width - espacos);
        for (int i = 0; i < filhos.Count; i++)
        {
            filhos[i].Measure(new Size(larguras[i], disponivel.Height));
            altura = Math.Max(altura, filhos[i].DesiredSize.Height);
        }
        return new Size(larguras.Sum() + espacos, altura);
    }

    protected override Size ArrangeOverride(Size tamanho)
    {
        List<UIElement> filhos = Visiveis;
        double espacos = Espaco * Math.Max(0, filhos.Count - 1);
        double[] larguras = Larguras(filhos.Select(f => f.DesiredSize.Width).ToArray(), tamanho.Width - espacos);
        double x = 0;
        for (int i = 0; i < filhos.Count; i++)
        {
            filhos[i].Arrange(new Rect(x, 0, larguras[i], tamanho.Height));
            x += larguras[i] + Espaco;
        }
        return tamanho;
    }

    /// <summary>
    /// Quanto cada um recebe do espaço: do menor para o maior, cada um pega o que quer, até a sua parte do que sobrou
    /// (sem espaço infinito, todos pegam o que querem).
    /// </summary>
    public static double[] Larguras(double[] desejadas, double espaco)
    {
        var larguras = new double[desejadas.Length];
        if (double.IsInfinity(espaco))
        {
            desejadas.CopyTo(larguras, 0);
            return larguras;
        }

        double sobra = Math.Max(0, espaco);
        int[] ordem = Enumerable.Range(0, desejadas.Length).OrderBy(i => desejadas[i]).ToArray();
        for (int n = 0; n < ordem.Length; n++)
        {
            int i = ordem[n];
            larguras[i] = Math.Min(desejadas[i], sobra / (ordem.Length - n));
            sobra -= larguras[i];
        }
        return larguras;
    }
}
