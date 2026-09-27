namespace OlhoNoChat;

using System.Windows;
using System.Windows.Media;

/// <summary>
/// Rounded window corners. The WPF content is clipped to the rounded shape; the chat itself
/// (WebView2) is transparent, so its square corners never show. The frame line is only drawn
/// in the accent color while the scroll mode is on with the borders hidden.
/// </summary>
public partial class MainWindow
{
    private Brush AccentBrush => (Brush)FindResource("BarAccentBrush");

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void SetWindowFrame(Brush brush, Thickness thickness)
    {
        this.WindowFrame.BorderBrush = brush;
        this.WindowFrame.BorderThickness = thickness;
        UpdateContentClip();
    }

    // The corner radius is the one of WindowFrame in MainWindow.xaml
    private void UpdateContentClip()
    {
        double radius = Math.Max(0, this.WindowFrame.CornerRadius.TopLeft - this.WindowFrame.BorderThickness.Left);
        var bounds = new Rect(0, 0, this.mainWindowGrid.ActualWidth, this.mainWindowGrid.ActualHeight);
        var clip = new RectangleGeometry(bounds, radius, radius);
        clip.Freeze();
        this.mainWindowGrid.Clip = clip;
    }
}
