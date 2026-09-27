namespace OlhoNoChat;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

/// <summary>
/// The bottom-right corner resizes the window in both directions from a 16 px square (with the borders
/// shown), not only from the few pixels where the side and bottom edges meet. The chat page is its own
/// window and takes the mouse, so its region leaves that square free; the square shows a small grip.
/// </summary>
public partial class MainWindow
{
    private const double CornerGripSize = 16;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTBOTTOMRIGHT = 17;
    private const int RGN_DIFF = 4;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    private bool _chatCornerCut = false;

    private void StartResizeCorner()
    {
        this.SourceInitialized += (s, e) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeCornerHook);
    }

    private bool ResizeCornerActive => !_hiddenBorders && this.ResizeMode != ResizeMode.NoResize;

    private IntPtr ResizeCornerHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST || !ResizeCornerActive)
            return IntPtr.Zero;

        long value = lParam.ToInt64();
        var screenPoint = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        Point point = PointFromScreen(screenPoint);

        if (point.X >= this.ActualWidth - CornerGripSize && point.Y >= this.ActualHeight - CornerGripSize)
        {
            handled = true;
            return new IntPtr(HTBOTTOMRIGHT);
        }

        return IntPtr.Zero;
    }

    // Leaves the corner square free of the chat page while the page reaches the bottom of the window
    // (borders shown, message box closed); the grip shows there
    private void UpdateResizeCorner()
    {
        bool cut = ResizeCornerActive && this.MessageBar.Visibility != Visibility.Visible;
        this.ResizeCornerGrip.Visibility = cut ? Visibility.Visible : Visibility.Collapsed;

        if (this.webView == null || this.webView.Handle == IntPtr.Zero)
            return;

        if (!cut)
        {
            if (_chatCornerCut)
                SetWindowRgn(this.webView.Handle, IntPtr.Zero, true);
            _chatCornerCut = false;
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(this.webView.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(this.webView.ActualHeight * dpi.DpiScaleY);
        int cutWidth = (int)Math.Ceiling((CornerGripSize - ResizeEdgeMargin.Right) * dpi.DpiScaleX);
        int cutHeight = (int)Math.Ceiling((CornerGripSize - ResizeEdgeMargin.Bottom) * dpi.DpiScaleY);

        IntPtr region = CreateRectRgn(0, 0, width, height);
        IntPtr corner = CreateRectRgn(width - cutWidth, height - cutHeight, width, height);
        CombineRgn(region, region, corner, RGN_DIFF);
        DeleteObject(corner);

        // The window owns the region from here on
        if (SetWindowRgn(this.webView.Handle, region, true) == 0)
            DeleteObject(region);
        else
            _chatCornerCut = true;
    }
}
