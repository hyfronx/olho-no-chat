namespace OlhoNoChat.Helpers;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

/// <summary>
/// The frame of every app window, the same everywhere. ModernWpf draws the window's own minimize, maximize
/// and close buttons over the title bar: they are hidden, and each window draws its own close button (style
/// TitleCloseButton in Styles/TitleBar.xaml) in the look of the app's other buttons. Windows 11 also draws
/// a thin gray line around normal windows: it gets the color of the window's edge (the shadow stays).
/// </summary>
public static class AppWindowFrame
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    // Once the window is loaded (its template and its handle exist)
    public static void Apply(Window window)
    {
        HideSystemButtons(window);

        // The see-through chat window draws its own rounded corners: any of these makes Windows draw its line there
        if (window.AllowsTransparency)
            return;

        // Windows 11 only (Windows 10 has neither and just refuses the attributes): rounded corners like the
        // chat window, and the line around in the color of the window's own edge, so it doesn't show
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        uint round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(uint));
        if (window.Background is SolidColorBrush { Color: var edge })
        {
            uint colorRef = edge.R | ((uint)edge.G << 8) | ((uint)edge.B << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(uint));
        }
    }

    private static void HideSystemButtons(Window window)
    {
        try
        {
            var titleBarControl = window.FindChildByType<DependencyObject>("ModernWpf.Controls.Primitives.TitleBarControl");
            if (titleBarControl == null)
                return;

            foreach (string name in new[] { "MinimizeButton", "PART_MaximizeRestoreButton", "CloseButton" })
            {
                if (titleBarControl.FindChild<Button>(name) is Button button)
                    button.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            // Only the title bar buttons: the window works the same if ModernWpf's template changes
        }
    }
}
