namespace OlhoNoChat;

using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

/// <summary>
/// Keeps the chat working when the embedded browser gets into a bad state:
/// - the disk cache is cleared before each navigation, since a corrupted cache entry
///   (e.g. a script failing with ERR_CONTENT_DECODING_FAILED) breaks the page;
/// - the "Padrão" page connects again by itself when the connection to Twitch drops; the watchdog only
///   reloads it when its script isn't running or its connection stays silent anyway;
/// - a crashed browser or chat page process is recovered without asking (no dialog over the game).
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan ChatWatchdogInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChatStartupGracePeriod = TimeSpan.FromSeconds(30);
    // The page itself replaces a connection silent for 75 s (browser/chat.js)
    private static readonly TimeSpan ChatSilenceLimit = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ChatNavigationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ChatRecoveryMinGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ChatRecoveryBackoffGap = TimeSpan.FromMinutes(5);
    private const int ChatMaxQuickRecoveries = 3;

    // A browser or chat page that keeps crashing is not recovered in a loop beside the game.
    // Each has its own count: only too many browser crashes close the app.
    private static readonly TimeSpan WebViewRecoveryWindow = TimeSpan.FromMinutes(5);
    private const int WebViewMaxRecoveries = 3;
    // An unresponsive page is reported every few seconds, also when the computer is only busy (e.g. a game loading)
    private static readonly TimeSpan ChatUnresponsiveLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ChatUnresponsiveEpisodeGap = TimeSpan.FromSeconds(60);

    // The state of the "Padrão" page's connection: "open:<ms since Twitch last sent something>", "disconnected"
    // (the page is connecting again) or "missing" (the page's script isn't running)
    private const string ChatConnectionProbeScript = """
        (function () {
            try {
                return window.oncChat ? window.oncChat.health() : 'missing';
            } catch (e) {
                return 'error:' + e.message;
            }
        })();
        """;

    private DispatcherTimer _chatWatchdogTimer;
    private bool _chatWatchdogBusy = false;
    private bool _chatNavigationPending = false;
    private DateTime _chatNavigationStartedAt = DateTime.MinValue;
    private DateTime _chatPageLoadedAt = DateTime.MinValue;
    private DateTime _lastChatRecoveryAt = DateTime.MinValue;
    private int _consecutiveChatRecoveries = 0;
    private readonly Queue<DateTime> _webViewRebuilds = new();
    private readonly Queue<DateTime> _chatPageReloads = new();
    private DispatcherTimer _chatPageReloadTimer;
    private DateTime? _chatUnresponsiveSince;
    private DateTime _chatUnresponsiveLastAt = DateTime.MinValue;

    private async Task ClearWebViewDiskCacheAsync()
    {
        try
        {
            CoreWebView2Profile profile = this.webView?.CoreWebView2?.Profile;
            if (profile != null)
                await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear the WebView2 disk cache.");
        }
    }

    private void OnChatNavigationStarting()
    {
        if (_composingInTwitchBox)
            EndCompose(returnFocus: false); // Twitch's box goes away with its page

        _chatPageReloadTimer?.Stop(); // a new page replaces the error page
        _chatNavigationPending = true;
        _chatNavigationStartedAt = DateTime.UtcNow;
        OnChatPageLoading();
    }

    private void OnChatNavigationCompleted()
    {
        _chatNavigationPending = false;
        _chatPageLoadedAt = DateTime.UtcNow;
        _chatUnresponsiveSince = null;
    }

    private void StartChatWatchdog()
    {
        if (_chatWatchdogTimer != null)
            return;

        _chatWatchdogTimer = new DispatcherTimer { Interval = ChatWatchdogInterval };
        _chatWatchdogTimer.Tick += ChatWatchdogTimer_Tick;
        _chatWatchdogTimer.Start();
    }

    private async void ChatWatchdogTimer_Tick(object sender, EventArgs e)
    {
        if (_chatWatchdogBusy)
            return;

        _chatWatchdogBusy = true;
        try
        {
            await CheckChatHealthAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat watchdog check failed.");
        }
        finally
        {
            _chatWatchdogBusy = false;
        }
    }

    private async Task CheckChatHealthAsync()
    {
        if (_currentChat?.ChatType != ChatTypes.Padrao || this.webView?.CoreWebView2 == null)
            return;

        DateTime now = DateTime.UtcNow;
        if (_chatNavigationPending && now - _chatNavigationStartedAt < ChatNavigationTimeout)
            return;

        string state = await ProbeChatConnectionAsync();

        if (state.StartsWith("open:"))
        {
            if (double.TryParse(state.AsSpan(5), out double silentMs) && silentMs > ChatSilenceLimit.TotalMilliseconds)
            {
                RecoverChat($"no data from Twitch for {silentMs / 1000:0}s");
                return;
            }

            _consecutiveChatRecoveries = 0;
        }
        else if (state != "disconnected" && now - _chatPageLoadedAt > ChatStartupGracePeriod)
        {
            // "missing" or "error": the page's script isn't running (a lost connection is the page's job)
            RecoverChat($"chat page not running ({state})");
        }
    }

    private async Task<string> ProbeChatConnectionAsync()
    {
        string json = await this.webView.CoreWebView2.ExecuteScriptAsync(ChatConnectionProbeScript);
        try
        {
            return JsonSerializer.Deserialize<string>(json) ?? "missing";
        }
        catch (JsonException)
        {
            return "missing";
        }
    }

    private void RecoverChat(string reason)
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan minGap = _consecutiveChatRecoveries >= ChatMaxQuickRecoveries ? ChatRecoveryBackoffGap : ChatRecoveryMinGap;
        if (now - _lastChatRecoveryAt < minGap)
            return;

        _lastChatRecoveryAt = now;
        _consecutiveChatRecoveries++;
        _logger.LogWarning("Chat watchdog: {Reason}. Reloading chat (attempt {Attempt}).", reason, _consecutiveChatRecoveries);

        // Navigating again also clears the disk cache (see NavigateToUrl).
        SetChatAddress(SavedChannel);
    }

    // WebView2 restarts its GPU, utility and other helper processes by itself: only a failed browser
    // process or chat page needs the app. Nothing is asked, so no dialog pops up over the game.
    private async void webView_CoreWebView2ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var kind = e.ProcessFailedKind;
        _logger.LogWarning("WebView2 process failed: {Kind}, reason {Reason}, exit code {ExitCode}.", kind, e.Reason, e.ExitCode);

        switch (kind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                await RecreateWebViewAsync();
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                ReloadChatAfterFailure(retryLater: true);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                // Reported again while the page stays unresponsive, so no retry is scheduled
                if (ChatUnresponsiveForLong())
                    ReloadChatAfterFailure(retryLater: false);
                break;
        }
    }

    private async Task RecreateWebViewAsync()
    {
        _chatPageReloadTimer?.Stop(); // the new browser loads the chat itself
        if (!TryCountRecovery(_webViewRebuilds, out _))
        {
            _atalhos.Ligados = false; // the old chat can't be used anymore
            await Task.Yield(); // no dialog inside the WebView2 event
            MessageBox.Show("Erro grave: o navegador interno parou várias vezes seguidas. O app será fechado.",
                "Falha na recuperação", MessageBoxButton.OK, MessageBoxImage.Stop);
            ExitApplication();
            return;
        }

        // Replaced right away: the old control throws on any use now (e.g. a hotkey)
        this.mainWindowGrid.Children.Remove(this.webView);
        this.webView.Dispose();
        WebView2EnvironmentManager.Reset();
        try
        {
            await SetupWebViewAsync(recreated: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not recreate the WebView2.");
            MessageBox.Show($"Erro grave: não foi possível recuperar o navegador interno. {ex.Message} O app será fechado.",
                "Falha na recuperação", MessageBoxButton.OK, MessageBoxImage.Stop);
            ExitApplication();
        }
    }

    // A new chat page process was started on an error page: the chat page is loaded again.
    // retryLater: after too many reloads, the page is reloaded once another one is allowed.
    private void ReloadChatAfterFailure(bool retryLater)
    {
        if (!TryCountRecovery(_chatPageReloads, out TimeSpan retryIn))
        {
            _logger.LogWarning("Too many chat page failures; not reloading the chat now.");
            if (retryLater)
                ScheduleChatPageReload(retryIn);
            return;
        }

        try
        {
            OnChatNavigationStarting();
            this.webView.CoreWebView2.Reload();
        }
        catch (Exception ex)
        {
            _chatNavigationPending = false;
            _logger.LogWarning(ex, "Could not reload the chat after a WebView2 failure.");
        }
    }

    // True once the page has been unresponsive for ChatUnresponsiveLimit
    private bool ChatUnresponsiveForLong()
    {
        DateTime now = DateTime.UtcNow;
        if (_chatUnresponsiveSince == null || now - _chatUnresponsiveLastAt > ChatUnresponsiveEpisodeGap)
            _chatUnresponsiveSince = now;
        _chatUnresponsiveLastAt = now;

        if (now - _chatUnresponsiveSince.Value < ChatUnresponsiveLimit)
            return false;
        _chatUnresponsiveSince = null;
        return true;
    }

    // Stopped by any navigation (see OnChatNavigationStarting)
    private void ScheduleChatPageReload(TimeSpan delay)
    {
        if (_chatPageReloadTimer == null)
        {
            _chatPageReloadTimer = new DispatcherTimer();
            _chatPageReloadTimer.Tick += (_, _) =>
            {
                _chatPageReloadTimer.Stop();
                ReloadChatAfterFailure(retryLater: true);
            };
        }
        _chatPageReloadTimer.Interval = delay + TimeSpan.FromSeconds(1);
        _chatPageReloadTimer.Start();
    }

    // False when WebViewMaxRecoveries were already counted within WebViewRecoveryWindow;
    // retryIn is then the time until the oldest one leaves the window.
    private static bool TryCountRecovery(Queue<DateTime> recoveries, out TimeSpan retryIn)
    {
        DateTime now = DateTime.UtcNow;
        while (recoveries.Count > 0 && now - recoveries.Peek() > WebViewRecoveryWindow)
            recoveries.Dequeue();

        if (recoveries.Count >= WebViewMaxRecoveries)
        {
            retryIn = recoveries.Peek() + WebViewRecoveryWindow - now;
            return false;
        }
        recoveries.Enqueue(now);
        retryIn = TimeSpan.Zero;
        return true;
    }
}
