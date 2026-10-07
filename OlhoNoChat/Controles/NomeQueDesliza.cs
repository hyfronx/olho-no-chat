using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OlhoNoChat.Controles;

/// <summary>
/// Um nome cortado ("meucanal…") que desliza devagar para mostrar o resto, e volta, enquanto o mouse está em cima da faixa.
/// Só nessa hora: uma animação redesenha a janela a cada quadro, também por cima do jogo. O TextBlock fica sozinho num
/// Grid com ClipToBounds (a moldura que corta); enquanto desliza, uma cópia inteira dele anda dentro da moldura e ele fica
/// invisível, guardando o lugar.
/// </summary>
public static class NomeQueDesliza
{
    private const double PixelsPorSegundo = 35;
    private static readonly TimeSpan Parado = TimeSpan.FromSeconds(0.8);

    /// <summary>Começa a deslizar, se o nome estiver cortado.</summary>
    public static void Comecar(TextBlock texto)
    {
        if (texto.Parent is not Grid moldura || moldura.Children.Count > 1)
            return;
        double visivel = moldura.ActualWidth;
        double inteiro = LarguraInteira(texto);
        if (visivel <= 0 || inteiro - visivel < 1)
            return;

        double distancia = inteiro - visivel;
        TimeSpan indo = TimeSpan.FromSeconds(distancia / PixelsPorSegundo);
        var animacao = new DoubleAnimationUsingKeyFrames { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        animacao.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Parado)));
        animacao.KeyFrames.Add(new LinearDoubleKeyFrame(-distancia, KeyTime.FromTimeSpan(Parado + indo)));
        animacao.KeyFrames.Add(new LinearDoubleKeyFrame(-distancia, KeyTime.FromTimeSpan(Parado + indo + Parado)));
        Timeline.SetDesiredFrameRate(animacao, 30);

        var deslocamento = new TranslateTransform();
        var copia = new TextBlock
        {
            Text = texto.Text,
            FontFamily = texto.FontFamily,
            FontSize = texto.FontSize,
            FontWeight = texto.FontWeight,
            Foreground = texto.Foreground,
            RenderTransform = deslocamento,
        };
        // Num Canvas a cópia não é cortada pelo próprio tamanho: só a moldura corta
        var trilho = new Canvas { IsHitTestVisible = false };
        Canvas.SetTop(copia, (moldura.ActualHeight - texto.ActualHeight) / 2 + texto.Margin.Top);
        trilho.Children.Add(copia);
        moldura.Children.Add(trilho);
        texto.Opacity = 0;
        deslocamento.BeginAnimation(TranslateTransform.XProperty, animacao);
    }

    /// <summary>Para e volta ao nome cortado com "…".</summary>
    public static void Parar(TextBlock texto)
    {
        if (texto.Parent is not Grid moldura)
            return;
        while (moldura.Children.Count > 1)
            moldura.Children.RemoveAt(1);
        texto.Opacity = 1;
    }

    private static double LarguraInteira(TextBlock texto)
    {
        var formatado = new FormattedText(texto.Text, CultureInfo.CurrentUICulture, texto.FlowDirection,
            new Typeface(texto.FontFamily, texto.FontStyle, texto.FontWeight, texto.FontStretch), texto.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(texto).PixelsPerDip);
        return Math.Ceiling(formatado.WidthIncludingTrailingWhitespace);
    }
}
