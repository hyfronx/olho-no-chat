namespace OlhoNoChat;

using OlhoNoChat.Sistema;
using System.Windows.Interop;
using System.Windows.Threading;

/// <summary>
/// "Sempre no topo" (the pin switch of the title bar, its hotkey and the tray menu). On, the chat
/// stays above games that make their own window "always on top" when they get focus (e.g. Hunt: Showdown):
/// both windows are then top-most and the last one activated wins. Whenever another app takes the focus,
/// and every second while a top-most window of another app has the focus, the chat is moved back to the
/// front — without ever taking the focus. Off, the chat is a normal window.
/// </summary>
public partial class MainWindow
{
    // Games may raise themselves a moment after they get the focus, so re-check a few times.
    private static readonly int[] ReassertDelaysMs = { 0, 150, 500, 1200, 2500 };

    private VigiaDeFoco _vigiaDeFoco;
    private DispatcherTimer _keepOnTopTimer;
    private bool _settingsDialogOpen = false;

    private static bool AlwaysOnTop => App.Settings.GeneralSettings.AlwaysOnTop;

    private void StartKeepOnTopGuard()
    {
        _vigiaDeFoco = new VigiaDeFoco();
        _vigiaDeFoco.OutroProgramaGanhouOFoco += ReassertTopMostBurst;

        _keepOnTopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _keepOnTopTimer.Tick += (s, e) =>
        {
            if (JanelaDoWindows.FocoEmOutroProgramaSempreNaFrente())
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
    // being top-most for a moment. A normal window (switched off) just gets activated.
    private void ActivateChatWindow()
    {
        this.Topmost = false;
        this.Activate();
        this.Topmost = AlwaysOnTop;
    }

    private void StopKeepOnTopGuard()
    {
        _keepOnTopTimer?.Stop();
        _vigiaDeFoco?.Dispose();
        _vigiaDeFoco = null;
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

    // Moves the chat back to the front without activating it.
    private void ReassertTopMost()
    {
        if (App.IsShuttingDown || _settingsDialogOpen || !AlwaysOnTop)
            return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !this.IsVisible)
            return;

        JanelaDoWindows.ColocarNaFrente(hwnd);
    }
}
