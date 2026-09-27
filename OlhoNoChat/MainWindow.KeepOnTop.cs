namespace OlhoNoChat;

using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

/// <summary>
/// "Sempre no topo" (the "Topo" switch of the side toolbar, its hotkey and the tray menu). On, the chat
/// stays above games that make their own window "always on top" when they get focus (e.g. Hunt: Showdown):
/// both windows are then top-most and the last one activated wins. Whenever another app takes the focus,
/// and every second while a top-most window of another app has the focus, the chat is moved back to the
/// front — without ever taking the focus. Off, the chat is a normal window.
/// </summary>
public partial class MainWindow
{
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const long WS_EX_TOPMOST = 0x00000008;

    // Games may raise themselves a moment after they get the focus, so re-check a few times.
    private static readonly int[] ReassertDelaysMs = { 0, 150, 500, 1200, 2500 };

    private WinEventProc _foregroundHookProc; // kept in a field so it isn't garbage collected
    private IntPtr _foregroundHook = IntPtr.Zero;
    private DispatcherTimer _keepOnTopTimer;
    private bool _settingsDialogOpen = false;

    private static bool AlwaysOnTop => App.Settings.GeneralSettings.AlwaysOnTop;

    private void StartKeepOnTopGuard()
    {
        _foregroundHookProc = OnForegroundChanged;
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundHookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        _keepOnTopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _keepOnTopTimer.Tick += (s, e) =>
        {
            if (IsForegroundTopMostOtherApp())
                ReassertTopMost();
        };
        ApplyAlwaysOnTop();

        this.Closed += (s, e) => StopKeepOnTopGuard();
    }

    private void ToggleAlwaysOnTop()
    {
        App.Settings.GeneralSettings.AlwaysOnTop = !AlwaysOnTop;

        // Save right away: otherwise the change is lost if the app does not close normally
        App.Settings.Persist();
        ApplyAlwaysOnTop();
    }

    // Also after "Restaurar tudo para o padrão"
    private void ApplyAlwaysOnTop()
    {
        bool on = AlwaysOnTop;
        this.Topmost = on;

        if (on)
        {
            _keepOnTopTimer.Start();
            ReassertTopMost();
        }
        else
            _keepOnTopTimer.Stop(); // nothing to check while it is a normal window

        btnQuickTop.IsChecked = on;
        menuAlwaysOnTop.IsChecked = on;
        UpdateHotkeyTooltips();
    }

    // Activate() alone doesn't bring the chat in front of a top-most game window: it has to stop
    // being top-most for a moment. A normal window ("Topo" off) just gets activated.
    private void ActivateChatWindow()
    {
        this.Topmost = false;
        this.Activate();
        this.Topmost = AlwaysOnTop;
    }

    private void StopKeepOnTopGuard()
    {
        _keepOnTopTimer?.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        ReassertTopMostBurst();
    }

    // Also after the chat changes (borders, scroll mode): a game may take the front back a moment later
    private async void ReassertTopMostBurst()
    {
        if (!AlwaysOnTop)
            return;

        int elapsed = 0;
        foreach (int delay in ReassertDelaysMs)
        {
            await Task.Delay(delay - elapsed);
            elapsed = delay;
            ReassertTopMost();
        }
    }

    private bool IsForegroundTopMostOtherApp()
    {
        IntPtr foreground = WindowHelper.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        WindowHelper.GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            return false;

        return (WindowHelper.GetWindowLongPtr(foreground, WindowHelper.GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
    }

    // Moves the chat back to the front without activating it.
    private void ReassertTopMost()
    {
        if (App.IsShuttingDown || _settingsDialogOpen || !AlwaysOnTop)
            return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !this.IsVisible)
            return;

        WindowHelper.SetWindowPosTopMost(hwnd);
    }
}
