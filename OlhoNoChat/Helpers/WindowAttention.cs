using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;

namespace OlhoNoChat.Helpers;

/// <summary>
/// While a window like Configurações is open, Windows keeps the chat disabled: a click on the chat
/// does nothing. This finds that open window, brings it to the front and blinks its orange frame.
/// </summary>
public static class WindowAttention
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const int SW_RESTORE = 9;
    private const uint FLASHW_ALL = 0x0003;

    private static readonly Color BlinkColor = Color.FromRgb(0xFF, 0xE4, 0xD8);
    private static readonly TimeSpan BlinkLength = TimeSpan.FromMilliseconds(340);
    private const int BlinkCount = 3;
    private const string OutlineTag = "WindowAttentionOutline";

    /// <summary>
    /// Calls attention to the window that keeps <paramref name="disabledWindow"/> disabled.
    /// Returns false if there is no such window.
    /// </summary>
    public static bool CallAttentionToOpenDialog(IntPtr disabledWindow)
    {
        IntPtr dialog = FindOpenDialog(disabledWindow);
        if (dialog == IntPtr.Zero)
            return false;

        if (IsIconic(dialog))
            ShowWindow(dialog, SW_RESTORE);

        // To the top of the "always on top" windows, then take the focus if Windows allows it
        WindowHelper.SetWindowPosTopMost(dialog, activate: true);
        WindowHelper.SetForegroundWindow(dialog);

        if (HwndSource.FromHwnd(dialog)?.RootVisual is Window window && Blink(window))
            return true;

        // Other windows (e.g. a message box): the standard Windows blink
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = dialog,
            dwFlags = FLASHW_ALL,
            uCount = BlinkCount,
            dwTimeout = 0
        };
        FlashWindowEx(ref info);
        return true;
    }

    // The front-most visible and enabled window of the app (a WPF window or a message box)
    private static IntPtr FindOpenDialog(IntPtr disabledWindow)
    {
        uint thread = GetCurrentThreadId();
        IntPtr found = IntPtr.Zero;

        // EnumWindows goes from the front-most window to the back
        EnumWindows((hwnd, lParam) =>
        {
            if (hwnd == disabledWindow || WindowHelper.GetWindowThreadProcessId(hwnd, out _) != thread
                || !IsWindowVisible(hwnd) || !IsWindowEnabled(hwnd))
                return true;

            // Skips tooltips, menus and the chat window
            bool isDialog = HwndSource.FromHwnd(hwnd)?.RootVisual is Window window
                ? window is not MainWindow
                : GetClassNameOf(hwnd) == "#32770";

            if (!isDialog)
                return true;

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static string GetClassNameOf(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    // Blinks the orange frame (the background of the window's root panel, as in Configurações and
    // Filtros do chat) and a light outline around the whole window. Returns false for other windows.
    private static bool Blink(Window window)
    {
        if (window.Content is not Grid frame || frame.Background is not SolidColorBrush brush
            || Application.Current.TryFindResource("SettingsAccentBrush") is not SolidColorBrush accent
            || (Color)brush.GetAnimationBaseValue(SolidColorBrush.ColorProperty) != accent.Color)
            return false;

        if (brush.IsFrozen)
        {
            brush = brush.Clone();
            frame.Background = brush;
        }

        // Added on the first blink and kept (invisible) afterwards; clicking again while it blinks starts over
        var outline = frame.Children.OfType<Border>().FirstOrDefault(b => OutlineTag.Equals(b.Tag));
        if (outline == null)
        {
            outline = new Border
            {
                Tag = OutlineTag,
                BorderBrush = new SolidColorBrush(BlinkColor),
                BorderThickness = new Thickness(3),
                IsHitTestVisible = false,
                Opacity = 0
            };
            Grid.SetRowSpan(outline, Math.Max(1, frame.RowDefinitions.Count));
            Grid.SetColumnSpan(outline, Math.Max(1, frame.ColumnDefinitions.Count));
            Panel.SetZIndex(outline, int.MaxValue);
            frame.Children.Add(outline);
        }

        var frameColor = (Color)brush.GetAnimationBaseValue(SolidColorBrush.ColorProperty);
        var colorAnimation = new ColorAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var outlineAnimation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };

        for (int i = 0; i < BlinkCount; i++)
        {
            TimeSpan start = TimeSpan.FromTicks(BlinkLength.Ticks * i);
            TimeSpan lit = start + TimeSpan.FromMilliseconds(70);
            TimeSpan fade = start + TimeSpan.FromMilliseconds(170);
            TimeSpan end = start + BlinkLength;

            colorAnimation.KeyFrames.Add(new LinearColorKeyFrame(frameColor, KeyTime.FromTimeSpan(start)));
            colorAnimation.KeyFrames.Add(new LinearColorKeyFrame(BlinkColor, KeyTime.FromTimeSpan(lit)));
            colorAnimation.KeyFrames.Add(new LinearColorKeyFrame(BlinkColor, KeyTime.FromTimeSpan(fade)));
            colorAnimation.KeyFrames.Add(new LinearColorKeyFrame(frameColor, KeyTime.FromTimeSpan(end)));

            outlineAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(start)));
            outlineAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(lit)));
            outlineAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(fade)));
            outlineAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(end)));
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
        outline.BeginAnimation(UIElement.OpacityProperty, outlineAnimation);
        return true;
    }
}
