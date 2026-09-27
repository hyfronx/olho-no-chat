using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Velopack;
using Velopack.Sources;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace OlhoNoChat;

using Chats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NHotkey;
using NHotkey.Wpf;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Threading;
using OlhoNoChat.Helpers;
using OlhoNoChat.Twitch;
using OlhoNoChat.Utils;
using OlhoNoChat.View;

/// <summary>
/// The chat window: the chat page (WebView2) over the game, with its borders, side toolbar and tray menu.
/// The other parts are in the MainWindow.*.cs files.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MainWindow> _logger;
    private TwitchService _twitchService; // created once redemptions are turned on (see EnsureTwitchService)
    private readonly TwitchAccount _twitchAccount;

    private WebView2 webView;
    private bool hasWebView2Runtime = false;

    private DispatcherTimer _timerCheckWebView2Install;

    private readonly Thickness noBorderThickness = new Thickness(0);

    private bool _hiddenBorders = false;
    private WindowDisplayMode CurrentDisplayMode = WindowDisplayMode.Setup;

    private readonly ChatSoundPlayer _chatSound = new();
    private Chat _currentChat = new WelcomeChat();
    private Button _closeButton;

    public MainWindow(IServiceProvider serviceProvider, ILogger<MainWindow> logger, TwitchAccount twitchAccount)
    {
        InitializeComponent();

        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchAccount = twitchAccount ?? throw new ArgumentNullException(nameof(twitchAccount));

        App.Settings.Tracker.Configure<MainWindow>()
            .Id(w => w.GetType().Name + "_State", null, false)
            .Properties(w => new { w.Top, w.Width, w.Height, w.Left, w.WindowState })
            .PersistOn(nameof(Window.Closing))
            .StopTrackingOn(nameof(Window.Closing));
        App.Settings.Tracker.Track(this);

        SetupOrReplaceHotkeys();

        InitializeWebViewAsync();
        StartChatWatchdog();
        StartKeepOnTopGuard();
        StartDialogAttention();
        StartChatInput();
        mainWindowGrid.SizeChanged += (s, e) => UpdateContentClip();
    }

    // Channel point redemptions appear in the chat (TwitchService already logs them)
    private void OnChannelPointsRewardRedeemed(object sender, TwitchLib.EventSub.Websockets.Core.EventArgs.Channel.ChannelPointsCustomRewardRedemptionArgs e)
    {
        var payloadEvent = e.Notification.Payload.Event;
        int cost = payloadEvent.Reward.Cost;
        string points = cost.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + (cost == 1 ? " ponto" : " pontos");

        PushNewChatMessageDispatcherInvoke($"resgatou \"{payloadEvent.Reward.Title}\" ({points})", payloadEvent.UserName, "#a1b3c4");

        if (!string.IsNullOrEmpty(payloadEvent.UserInput))
            PushNewChatMessageDispatcherInvoke(payloadEvent.UserInput, payloadEvent.UserName, "#a1b3c4");
    }

    private void SetupOrReplaceHotkeys()
    {
        HotkeyManager.Current.Remove("ToggleBorders");
        HotkeyManager.Current.Remove("ToggleInteraction");
        HotkeyManager.Current.Remove("AlwaysOnTop");
        HotkeyManager.Current.Remove("WriteMessage");
        _hotkeysToRetry.Clear();

        var settings = App.Settings.GeneralSettings;
        RegisterHotkey("ToggleBorders", settings.ToggleBordersHotkey, OnHotKeyToggleBorders);
        RegisterHotkey("ToggleInteraction", settings.ToggleInteractableHotkey, OnHotKeyToggleInteraction);
        RegisterHotkey("AlwaysOnTop", settings.BringToTopHotkey, OnHotKeyToggleAlwaysOnTop);
        RegisterHotkey("WriteMessage", settings.WriteMessageHotkey, OnHotKeyWriteMessage);

        UpdateHotkeyTooltips();
    }

    // Keyboard shortcuts work for one program at a time. One that another program is using right now
    // (e.g. a second Olho no Chat that was open first) is tried again every few seconds, so it starts
    // working as soon as that program lets it go.
    private static readonly TimeSpan HotkeyRetryInterval = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, (Hotkey Hotkey, EventHandler<HotkeyEventArgs> Handler)> _hotkeysToRetry = new();
    private DispatcherTimer _hotkeyRetryTimer;

    private bool RegisterHotkey(string name, Hotkey hotkey, EventHandler<HotkeyEventArgs> handler)
    {
        if (hotkey == null || hotkey.Key == Key.None)
            return false;

        try
        {
            HotkeyManager.Current.AddOrReplace(name, hotkey.Key, hotkey.Modifiers, handler);
            _hotkeysToRetry.Remove(name);
            return true;
        }
        catch (Exception ex)
        {
            if (!_hotkeysToRetry.ContainsKey(name))
                _logger.LogWarning(ex, "Hotkey {Name} ({Hotkey}) is in use by another program; trying again every few seconds.", name, hotkey);
            _hotkeysToRetry[name] = (hotkey, handler);

            if (_hotkeyRetryTimer == null)
            {
                _hotkeyRetryTimer = new DispatcherTimer { Interval = HotkeyRetryInterval };
                _hotkeyRetryTimer.Tick += (s, e) => RetryHotkeys();
            }
            _hotkeyRetryTimer.Start();
            return false;
        }
    }

    private void RetryHotkeys()
    {
        foreach (var (name, pending) in _hotkeysToRetry.ToList())
        {
            if (RegisterHotkey(name, pending.Hotkey, pending.Handler))
                _logger.LogInformation("Hotkey {Name} ({Hotkey}) works now.", name, pending.Hotkey);
        }

        if (_hotkeysToRetry.Count == 0)
            _hotkeyRetryTimer?.Stop();
    }

    private void OnHotKeyToggleInteraction(object sender, HotkeyEventArgs e)
    {
        ToggleInteractable();
        e.Handled = true;
    }

    private void OnHotKeyToggleAlwaysOnTop(object sender, HotkeyEventArgs e)
    {
        ToggleAlwaysOnTop();
        e.Handled = true;
    }

    private void OnHotKeyToggleBorders(object sender, HotkeyEventArgs e)
    {
        ToggleBorderVisibility();
        e.Handled = true;
    }


    private void CheckWebView2Timer_Tick(object sender, EventArgs e)
    {
        string version = "";
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(version))
            return;

        InitializeWebViewAsync();
    }

    private void ShowWebViewInstallUI()
    {
        hasWebView2Runtime = false;
        PlaceholderOverlay.Visibility = Visibility.Visible;
    }

    // Microsoft's small WebView2 installer, which downloads the rest itself
    private const string WebView2InstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var installButton = sender as Button;
        installButton.IsEnabled = false; // Disable button to prevent multiple clicks
        installButton.Content = "Baixando...";

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "OlhoNoChat");
            Directory.CreateDirectory(folder);
            string installerPath = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe");

            // The whole download (about 2 MB) is read within the timeout, so a stalled connection can't leave it stuck
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            {
                byte[] installer = await http.GetByteArrayAsync(WebView2InstallerUrl);
                await File.WriteAllBytesAsync(installerPath, installer);
            }

            // Run the installer and wait for it to finish.
            installButton.Content = "Instalando...";
            using var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            if (process != null)
                await process.WaitForExitAsync();

            installButton.Content = "Instalação concluída";
            PlaceHolderOverlayText.Text = "WebView2 instalado com sucesso! O app vai recarregar sozinho.";

            if (_timerCheckWebView2Install == null)
            {
                _timerCheckWebView2Install = new DispatcherTimer();
                _timerCheckWebView2Install.Interval = TimeSpan.FromSeconds(2.5);
                _timerCheckWebView2Install.Tick += CheckWebView2Timer_Tick;
                _timerCheckWebView2Install.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível baixar ou instalar o WebView2. Confira sua internet e tente de novo.\n\n{ex.Message}", "Falha na instalação");
            installButton.IsEnabled = true;
            installButton.Content = "Baixar e instalar";
        }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrEmpty(version))
            {
                ShowWebViewInstallUI();
                return;
            }
        }
        catch (Exception)
        {
            ShowWebViewInstallUI();
            return;
        }

        hasWebView2Runtime = true;
        if (_timerCheckWebView2Install != null)
        {
            _timerCheckWebView2Install.Stop();
            _timerCheckWebView2Install.Tick -= CheckWebView2Timer_Tick;
            _timerCheckWebView2Install = null;
        }

        // Make sure the placeholder overlay is hidden
        PlaceholderOverlay.Visibility = Visibility.Collapsed;

        await SetupWebViewAsync();
    }

    // recreated: after the browser process ended (see MainWindow.ChatResilience.cs). The new control takes
    // the old one's place as it was: the borders, the scroll mode and the focus of the game stay.
    private async Task SetupWebViewAsync(bool recreated = false)
    {
        // Create and configure.
        webView = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Transparent
        };
        if (recreated)
            webView.Focusable = CurrentDisplayMode == WindowDisplayMode.Setup;

        CoreWebView2Environment cwv2Environment = await WebView2EnvironmentManager.GetEnvironmentAsync();

        // Add to visual tree (the row span is set with the borders, see SetChatRowSpan).
        Grid.SetRow(webView, 2);
        if (recreated)
            Grid.SetRowSpan(webView, Grid.GetRowSpan(this.overlay));
        this.mainWindowGrid.Children.Add(webView);
        UpdateChatResizeEdge();

        // Initialize and subscribe to events.
        await webView.EnsureCoreWebView2Async(cwv2Environment);

        webView.NavigationCompleted += webView_NavigationCompleted;
        webView.WebMessageReceived += webView_WebMessageReceived;
        webView.CoreWebView2.ProcessFailed += webView_CoreWebView2ProcessFailed;
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(LocalHtmlHelper.ChatPageHost, LocalHtmlHelper.BrowserFolder,
            CoreWebView2HostResourceAccessKind.DenyCors);

        await AddTwemojiFallbackAsync(webView.CoreWebView2);
        ApplyLightweightWebViewSettings(webView.CoreWebView2);
        SetupChatLinks(webView.CoreWebView2);

        if (recreated)
            LoadChat();
        else
            SetupBrowser();
    }

    public void ProcessCommandLineArgs(string[] args)
    {
        // Check if the command is our special "show window" command
        if (args.Length > 0 && args[0] == IpcManager.ShowWindowCommand) {
            if (AlwaysOnTop)
                ReassertTopMost();
            else
                ActivateChatWindow();
            return;
        }

        foreach (var arg in args)
        {
            switch (arg.ToLower())
            {
                case "/toggleborders":
                    ToggleBorderVisibility();
                    break;
                case "/settings":
                    ShowSettingsWindow();
                    break;
                case "/resetwindow":
                    ResetWindowAndOfferSettingsFolder();
                    break;
            }
        }
    }

    // "Modo rolagem" (hotkey and tray menu)
    private void ToggleInteractable()
    {
        if (!hasWebView2Runtime) return;
        SetInteractable(CurrentDisplayMode != WindowDisplayMode.Setup);
    }

    private void SetInteractable(bool interactable)
    {
        ApplyInteractable(interactable);

        // Not over Configurações after "Salvar" (it stays open)
        if (interactable && !_settingsDialogOpen)
            ActivateChatWindow();

        UpdateChatScrollMode();
        ReassertTopMostBurst();
    }

    // The window takes clicks (setup mode) or lets them through to the game (overlay)
    private void ApplyInteractable(bool interactable)
    {
        if (this.webView != null)
            this.webView.Focusable = interactable;

        CurrentDisplayMode = interactable ? WindowDisplayMode.Setup : WindowDisplayMode.Overlay;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (interactable)
            WindowHelper.SetWindowExDefault(hwnd);
        else
            WindowHelper.SetWindowExTransparent(hwnd);

        ApplyBackgroundOpacity();

        // The title bar is shown/hidden only with the borders (drawBorders/hideBorders);
        // with hidden borders, a thin colored frame shows that the chat is taking clicks.
        if (_hiddenBorders)
        {
            SetWindowFrame(interactable ? AccentBrush : Brushes.Transparent, interactable ? scrollModeBorderThickness : noBorderThickness);

            if (!interactable && _scrollModeFromToolbar)
            {
                _scrollModeFromToolbar = false;
                RequestBordersHint();
            }
        }
    }

    private readonly Thickness scrollModeBorderThickness = new Thickness(2);

    // Lets the KapChat page scroll while the window takes clicks, and shows the "modo rolagem" hint
    // only when the borders are hidden (otherwise the window is simply in setup mode).
    private void UpdateChatScrollMode()
    {
        if (_currentChat?.ChatType != ChatTypes.KapChat || this.webView?.CoreWebView2 == null)
            return;

        bool enabled = this.webView.Focusable;
        Hotkey hotkey = App.Settings.GeneralSettings.ToggleInteractableHotkey;
        string hotkeyText = hotkey != null && hotkey.Key != Key.None ? hotkey.ToString() : string.Empty;

        string mode = System.Text.Json.JsonSerializer.Serialize(new { enabled, banner = enabled && _hiddenBorders, hotkey = hotkeyText });
        _ = this.webView.CoreWebView2.ExecuteScriptAsync(
            $"window.oncScrollModeWanted = {mode}; if (window.oncSetScrollMode) window.oncSetScrollMode(window.oncScrollModeWanted);");
    }

    // With the borders shown, the bottom edge of the window resizes it: the chat page (its own window,
    // which takes the mouse) stays a few pixels above it, over its dark background
    private static readonly Thickness ResizeEdgeMargin = new Thickness(0, 0, 0, 6);

    private void UpdateChatResizeEdge()
    {
        this.webView?.SetValue(MarginProperty, _hiddenBorders ? this.noBorderThickness : ResizeEdgeMargin);
    }

    private void drawBorders()
    {
        this.ShowInTaskbar = true;

        // Close button of the title bar (minimize/maximize are always hidden, see Window_Loaded)
        SetCloseButtonVisibility(true);

        this.AppTitleBar.Visibility = Visibility.Visible;
        this.QuickToolbar.Visibility = Visibility.Visible;
        SetChatRowSpan(1);
        SetWindowFrame(Brushes.Transparent, this.noBorderThickness);
        this.ResizeMode = ResizeMode.CanResizeWithGrip;

        _hiddenBorders = false;
        _scrollModeFromToolbar = false;
        UpdateChatResizeEdge();
        ApplyInteractable(App.Settings.GeneralSettings.AllowInteraction);

        // The title bar and the toolbar take clicks also when clicking the chat is off
        WindowHelper.SetWindowExDefault(new WindowInteropHelper(this).Handle);

        UpdateChatInput();
        UpdateChannelBar();
        UpdateChatLinks();
        UpdateChatScrollMode();
        HideBordersHint();

        ActivateChatWindow();
        ReassertTopMostBurst();
    }

    private void hideBorders()
    {
        if (_composeFromButton)
            EndCompose(returnFocus: false);

        if (App.Settings.GeneralSettings.HideTaskbarIcon)
            this.ShowInTaskbar = false;

        // Prevent interaction with the browser
        ApplyInteractable(false);

        // Close button of the title bar (minimize/maximize are always hidden, see Window_Loaded)
        SetCloseButtonVisibility(false);

        this.AppTitleBar.Visibility = Visibility.Collapsed;
        this.QuickToolbar.Visibility = Visibility.Collapsed;
        // The chat and its dark background cover the message box row too
        // (unless the message box is open with the hotkey, see MainWindow.ChatInput.cs)
        SetChatRowSpan(_composing ? 1 : 2);
        SetWindowFrame(Brushes.Transparent, this.noBorderThickness);
        this.ResizeMode = System.Windows.ResizeMode.NoResize;

        _hiddenBorders = true;
        _chatBoxOpen = false; // the box opened with "Escrever" starts closed again when the borders come back
        UpdateChatResizeEdge();
        UpdateChatInput();
        UpdateChannelBar();
        UpdateChatLinks();
        UpdateChatScrollMode();
        RequestBordersHint();

        ActivateChatWindow();
        ReassertTopMostBurst();
    }

    private void ToggleBorderVisibility()
    {
        if (!hasWebView2Runtime) return;

        if (_hiddenBorders)
            drawBorders();
        else
            hideBorders();
    }

    private void ResetWindowState()
    {
        drawBorders();
        this.WindowState = WindowState.Normal;
        this.Left = 10;
        this.Top = 10;
        this.Height = 500;
        this.Width = 320;
    }

    private async void NavigateToUrl(string url)
    {
        try
        {
            OnChatNavigationStarting();
            await ClearWebViewDiskCacheAsync();
            this.webView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            _chatNavigationPending = false;
            string urlStatus = string.IsNullOrEmpty(url) ? "<Empty>" : url;
            _logger.LogError(ex, "Failed to navigate to custom chat URL: " + urlStatus);
            MessageBox.Show($"Não foi possível abrir esse endereço.\nErro: {ex.Message}\nEndereço: '{urlStatus}'", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // The "Padrão" chat page of a channel (a name, see SavedChannel): the app's own page (browser\chat.html)
    private void SetChatAddress(string channel)
    {
        bool darkTheme = App.Settings.GeneralSettings.ThemeIndex != 0;
        NavigateToUrl(LocalHtmlHelper.GetChatPageUrl(channel, darkTheme));
    }

    private void ExitApplication()
    {
        App.IsShuttingDown = true;
        Application.Current.Shutdown();
    }

    private void MenuItem_ToggleBorderVisible(object sender, RoutedEventArgs e)
    {
        ToggleBorderVisibility();
    }

    private void MenuItem_CheckForUpdates(object sender, RoutedEventArgs e)
    {
        _ = CheckForUpdateAsync(notifyIfNoUpdate: true);
    }

    private void btnHide_Click(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;
        hideBorders();
    }

    private void MenuItem_ZoomIn(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;
        SetZoomFactor(App.Settings.GeneralSettings.ZoomLevel + 0.1);

        // Save right away: otherwise the change is lost if the app does not close normally
        App.Settings.Persist();
    }

    private void MenuItem_ZoomOut(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;
        SetZoomFactor(App.Settings.GeneralSettings.ZoomLevel - 0.1);

        // Save right away: otherwise the change is lost if the app does not close normally
        App.Settings.Persist();
    }

    private void MenuItem_ZoomReset(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;
        SetZoomFactor(GeneralSettings.DefaultZoomLevel);

        // Save right away: otherwise the change is lost if the app does not close normally
        App.Settings.Persist();
    }

    // The "Texto" value gives letters of the same size in the "Padrão" and "Chat oficial da Twitch"
    // chats: the official one uses the font and size of the "Padrão" (CustomURLChat.MessageLookCss).
    // Kept between 10% and 400%.
    private void SetZoomFactor(double zoom)
    {
        zoom = Math.Round(Math.Clamp(zoom, 0.1, 4), 2);

        this.webView.ZoomFactor = zoom;
        App.Settings.GeneralSettings.ZoomLevel = zoom;
        UpdateQuickToolbarValues();
    }

    // Current values shown under the +/- buttons of the side toolbar
    private void UpdateQuickToolbarValues()
    {
        this.btnTextSizeValue.Content = $"{Math.Round(App.Settings.GeneralSettings.ZoomLevel * 100)}%";
        this.btnBackgroundValue.Content = $"{Math.Round(App.Settings.GeneralSettings.OpacityLevel / 255.0 * 100)}%";
    }

    private async void webView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        OnChatNavigationCompleted();
        OnChatPageLoaded(e.IsSuccess);

        if (!e.IsSuccess)
        {
            _logger.LogWarning("Chat page failed to load: {Status}", e.WebErrorStatus);
            return;
        }

        SetZoomFactor(App.Settings.GeneralSettings.ZoomLevel);

        // Not on the welcome page, which has the same chat type setting but no Twitch chat
        if (_currentChat.ChatType == ChatTypes.TwitchPopout)
            TwitchPopoutSetup();

        // Our CSS and then the chat type's script, in one call: the first lines already get our look
        string css = this._currentChat.SetupCustomCSS();
        string script = (string.IsNullOrEmpty(css) ? string.Empty : InsertCustomCSS2(css) + "\n") + this._currentChat.SetupJavascript();
        if (!string.IsNullOrEmpty(script))
            await this.webView.ExecuteScriptAsync(script);
        UpdateChatScrollMode();
        UpdateChatLinks();
        TryShowBordersHint();
        TryShowWriteHint();

        // Channel point redemptions (after the saved Twitch access is checked)
        _ = StartRedemptionsAsync();
    }

    private async void TwitchPopoutSetup()
    {
        if (App.Settings.GeneralSettings.BetterTtv)
        {
            // BTTV's options, kept in the page's localStorage: "7TV emotes" is the flag 16 of emotes[0] (there
            // once BTTV has saved its emote options) and the emote menu is 0 off, 1 legacy, 2 modern.
            // Written on every load, so that turning an option off works too.
            string sevenTvFlag = App.Settings.GeneralSettings.BetterTtv_7tv
                ? "settings.emotes[0] = settings.emotes[0] | 16;"
                : "settings.emotes[0] = settings.emotes[0] & ~16;";
            int emoteMenu = App.Settings.GeneralSettings.BetterTtv_AdvEmoteMenu ? 2 : 0;

            var bttvSettingsScript = $$"""
                (function() {
                    try {
                        const settingsKey = 'bttv_settings';
                        let settings = JSON.parse(localStorage.getItem(settingsKey) || '{}');

                        if (settings.emotes && Array.isArray(settings.emotes)) {
                            {{sevenTvFlag}}
                        }
                        settings.emoteMenu = {{emoteMenu}};

                        localStorage.setItem(settingsKey, JSON.stringify(settings));
                        console.log('BTTV settings (7TV / emote menu) applied.');
                    } catch (e) {
                        console.error('Failed to pre-configure BTTV settings', e);
                    }
                })();
                """;

            await webView.CoreWebView2.ExecuteScriptAsync(bttvSettingsScript);

            // Inject the main BTTV script.
            InsertCustomJavaScriptFromUrl("https://cdn.betterttv.net/betterttv.js");
        }
        if (App.Settings.GeneralSettings.FrankerFaceZ)
        {
            // Observe for FrankerFaceZ's reskin stylesheet
            // that breaks the transparency and remove it
            InsertCustomJavaScript(@"
(function() {
    const head = document.getElementsByTagName(""head"")[0];
    const observer = new MutationObserver((mutations, observer) => {
        for (const mut of mutations) {
            if (mut.type === ""childList"") {
                for (const node of mut.addedNodes) {
                    if (node.tagName.toLowerCase() === ""link"" && node.href.includes(""color_normalizer"")) {
                        node.remove();
                    }
                }
            }
        }
    });
    observer.observe(head, {
        attributes: false,
        childList: true,
        subtree: false,
    });
})();
                        ");

            InsertCustomJavaScriptFromUrl("https://cdn.frankerfacez.com/static/script.min.js");
        }
    }

    // A single style element, updated in place when the settings change (see MainWindow.LiveSettings.cs)
    private string InsertCustomCSS2(string CSS)
    {
        string uriEncodedCSS = Uri.EscapeDataString(CSS);
        string script = "(function () { var oncCSS = document.getElementById('" + CustomCssElementId + "');";
        script += "if (!oncCSS) { oncCSS = document.createElement('style'); oncCSS.id = '" + CustomCssElementId + "'; document.querySelector('head').appendChild(oncCSS); }";
        script += "oncCSS.textContent = decodeURIComponent(\"" + uriEncodedCSS + "\"); })();";
        return script;
    }

    private async void InsertCustomJavaScript(string JS)
    {
        try
        {
            await this.webView.ExecuteScriptAsync(JS);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertCustomJavaScriptFromUrl(string scriptUrl)
    {
        InsertCustomJavaScript(@"
(function() {
    const script = document.createElement(""script"");
    script.src = """ + scriptUrl + @""";
    document.getElementsByTagName(""head"")[0].appendChild(script);
})();
            ");
    }

    private void OpenSettingsFolder()
    {
        string folderPath = (App.Settings.Tracker.Store as Jot.Storage.JsonFileStore).FolderPath;
        ShellHelper.OpenFolder(folderPath);
    }

    // The new-message sound of the "Padrão" chat, the only chat type that plays one. It follows the saved chat
    // type (the welcome page of the "Padrão" rings as soon as a channel is entered on the channel strip).
    // Also after every save: the sound, its volume or the output device may have changed.
    private void UpdateChatSound()
    {
        var settings = App.Settings.GeneralSettings;
        string file = string.Empty;
        if (settings.ChatType == (int)ChatTypes.KapChat && !settings.ChatNotificationSound.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            string path = Path.Combine(SoundFolder.Resolve(settings.SoundClipsFolder), settings.ChatNotificationSound);
            if (File.Exists(path))
                file = path;
        }

        // The output device plays the loaded file, so it is released first (set up again on the next sound)
        _chatSound.OnAudioDeviceChanged();
        _chatSound.SetMediaFile(file);
    }

    private void ShowSettingsWindow()
    {
        if (!hasWebView2Runtime)
        {
            MessageBox.Show(
                "Para usar o Olho no Chat, baixe e instale o WebView2 da Microsoft.\nDepois de instalar, abra o app de novo.",
                "WebView2 necessário",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShellHelper.OpenUrl(WebView2InstallerUrl);
            return;
        }

        // Disable hotkeys while settings window is open
        HotkeyManager.Current.IsEnabled = false;

        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();

        settingsWindow.CheckForUpdateRequested += () => {
            _ = CheckForUpdateAsync(notifyIfNoUpdate: true, settingsWindow);
        };

        // What the open chat page was loaded with, to know if a save needs to load it again
        int chatTypeLoaded = App.Settings.GeneralSettings.ChatType;
        string chatReloadKeyLoaded = GetChatReloadKey();

        // "Salvar" keeps the window open, so every save is applied right away
        settingsWindow.SettingsSaved += () =>
        {
            int chatTypeBefore = chatTypeLoaded;
            string chatReloadKeyBefore = chatReloadKeyLoaded;
            chatTypeLoaded = App.Settings.GeneralSettings.ChatType;
            chatReloadKeyLoaded = GetChatReloadKey();

            if (!IsChannelChatType(chatTypeBefore) && IsChannelChatType(chatTypeLoaded))
                RequestWriteHint();

            _ = ApplySavedSettingsAsync(chatTypeBefore, chatReloadKeyBefore);
        };

        _settingsDialogOpen = true;
        try
        {
            settingsWindow.ShowDialog();
        }
        finally
        {
            _settingsDialogOpen = false;
        }
        TryShowWriteHint();

        // Changes that were not saved never reach App.Settings, so there is nothing to undo
        HotkeyManager.Current.IsEnabled = true;
    }

    // Applies the settings just saved in the Settings (or Chat Filters) window
    private async Task ApplySavedSettingsAsync(int chatTypeBefore, string chatReloadKeyBefore)
    {
        // Loading the chat page again clears the messages on screen, so it only happens when a
        // setting needs it (channel, theme, chat type...). The rest is applied to the open page.
        bool reloadChat = App.Settings.GeneralSettings.ChatType != chatTypeBefore
            || GetChatReloadKey() != chatReloadKeyBefore
            || !await TryApplyChatSettingsLiveAsync();

        if (reloadChat)
            LoadChat();
        UpdateChatSound();

        // Channel point redemptions and the "Escrever no chat…" box (options of the Twitch tab)
        if (App.Settings.GeneralSettings.RedemptionsEnabled)
            _ = StartRedemptionsAsync();
        else
            _twitchService?.DisableEventSub();
        UpdateChatInput();
        UpdateChannelBar();

        // The taskbar button hides only with the borders (see hideBorders)
        this.ShowInTaskbar = !_hiddenBorders || !App.Settings.GeneralSettings.HideTaskbarIcon;

        // Text size, background and "Topo" of the side toolbar (changed here only by "Restaurar tudo para o padrão")
        SetZoomFactor(App.Settings.GeneralSettings.ZoomLevel);
        ApplyBackgroundOpacity();
        ApplyAlwaysOnTop();

        if (!this._hiddenBorders)
        {
            this.webView.Focusable = true;
            if (App.Settings.GeneralSettings.AllowInteraction)
                SetInteractable(true);
        }

        SetupOrReplaceHotkeys();
    }

    // The gear opens Configurações (left or right click)
    private void btnSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var titleBarControl = this.FindChildByType<DependencyObject>("ModernWpf.Controls.Primitives.TitleBarControl");
            if (titleBarControl != null)
            {
                _closeButton = titleBarControl.FindChild<Button>("CloseButton");

                // Only the close button makes sense for an overlay
                foreach (string name in new[] { "MinimizeButton", "PART_MaximizeRestoreButton" })
                {
                    if (titleBarControl.FindChild<Button>(name) is Button button)
                        button.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch
        {
            // Only the title bar buttons: the window works the same if ModernWpf's template changes
        }

        // Every time the app opens (it used to be once a day)
        if (App.Settings.GeneralSettings.CheckForUpdates)
        {
            _ = CheckForUpdateAsync();
        }
    }

    private void SetCloseButtonVisibility(bool isVisible)
    {
        if (_closeButton != null)
        {
            if (isVisible)
                _closeButton.Visibility = Visibility.Visible;
            else
                _closeButton.Visibility = Visibility.Collapsed;
        }
    }

    private async Task CheckForUpdateAsync(bool notifyIfNoUpdate = false, Window owner = null)
    {
#if DEBUG
        var dialog = new UpdateDialog("1.1.0", "1.1.5");
        dialog.Owner = owner;
        dialog.ShowDialog();
#else
        _logger.LogInformation("Checking for updates...");

        var mgr = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, null, false));

        try
        {
            var newVersion = await mgr.CheckForUpdatesAsync();

            if (newVersion == null)
            {
                _logger.LogInformation("No updates available.");
                if (notifyIfNoUpdate)
                {
                    MessageBox.Show("Você já está com a versão mais recente!", "Sem atualizações", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return; // no update available
            }

            string currentVersionStr = mgr.CurrentVersion?.ToString() ?? "0.0.0";
            string newVersionStr = newVersion.TargetFullRelease.Version.ToString();

            // Create and show the custom dialog
            var dialog = new UpdateDialog(currentVersionStr, newVersionStr);
            dialog.Owner = owner;

            // ShowDialog() pauses execution until the window is closed
            if (dialog.ShowDialog() == true)
            {
                // User clicked "Update Now"
                _logger.LogInformation($"Downloading and applying update to version {newVersionStr}...");
                await mgr.DownloadUpdatesAsync(newVersion);
                mgr.ApplyUpdatesAndRestart(newVersion);
            }
            else
            {
                // User clicked "Later" or closed the window.
                // Now, check if they want to disable future updates.
                if (dialog.ShouldDisableUpdates)
                {
                    _logger.LogInformation("User has disabled automatic update checks.");
                    App.Settings.GeneralSettings.CheckForUpdates = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates (Outer Exception)");

            if (ex.InnerException != null)
            {
                _logger.LogError(ex.InnerException, "INNER EXCEPTION DETAILS");
            }

            // The automatic check at startup fails silently (e.g. offline, or no release published yet).
            if (notifyIfNoUpdate)
            {
                MessageBox.Show("Não foi possível procurar atualizações agora. Confira sua internet e tente de novo mais tarde.\n\nDetalhes: " + ex.Message,
                    "Procurar atualizações", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
#endif
    }

    private void SetupBrowser()
    {
        if (App.Settings.GeneralSettings.ZoomLevel <= 0)
            App.Settings.GeneralSettings.ZoomLevel = GeneralSettings.DefaultZoomLevel;

        // The background is applied with the borders (see ApplyInteractable)
        if (App.Settings.GeneralSettings.AutoHideBorders)
            hideBorders();
        else
            drawBorders();

        UpdateChatSound();
        LoadChat();
    }

    /// <summary>
    /// Loads the page of the saved chat type: the channel chat, the custom address, or the welcome page
    /// when there is no channel or address.
    /// </summary>
    private void LoadChat()
    {
        var settings = App.Settings.GeneralSettings;

        if (ChatTypeUsesChannel)
        {
            LoadChannelChat();
        }
        else if (settings.ChatType == (int)ChatTypes.CustomURL && !string.IsNullOrWhiteSpace(settings.CustomURL))
        {
            _currentChat = new CustomURLChat(ChatTypes.CustomURL);
            NavigateToUrl(settings.CustomURL);
        }
        else
        {
            ShowWelcomePage();
        }
    }

    private void MenuItem_SettingsClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }

    private void MenuItem_Exit(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void MenuItem_ResetWindowClick(object sender, RoutedEventArgs e)
    {
        ResetWindowAndOfferSettingsFolder();
    }

    // Tray menu and jump list "Restaurar posição da janela"
    private void ResetWindowAndOfferSettingsFolder()
    {
        ResetWindowState();

        if (MessageBox.Show("Abrir a pasta de configurações?", "Pasta de configurações", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes)
        {
            OpenSettingsFolder();
        }
    }

    // The saved "Fundo" (0-255) on the dark background of the chat
    private void ApplyBackgroundOpacity()
    {
        double opacity = App.Settings.GeneralSettings.OpacityLevel / 255.0;

        // A 0% background still takes clicks in setup mode
        if (opacity <= 0 && this.CurrentDisplayMode == WindowDisplayMode.Setup)
            opacity = 0.01;

        this.overlay.Opacity = opacity;
        UpdateQuickToolbarValues();
    }

    private void SetOpacityLevel(int level)
    {
        if (!hasWebView2Runtime) return;

        App.Settings.GeneralSettings.OpacityLevel = (byte)Math.Clamp(level, 0, 255);
        ApplyBackgroundOpacity();

        // Save right away: otherwise the change is lost if the app does not close normally
        App.Settings.Persist();
    }

    private void MenuItem_IncOpacity(object sender, RoutedEventArgs e)
    {
        SetOpacityLevel(App.Settings.GeneralSettings.OpacityLevel + 15);
    }

    private void MenuItem_DecOpacity(object sender, RoutedEventArgs e)
    {
        SetOpacityLevel(App.Settings.GeneralSettings.OpacityLevel - 15);
    }

    private void MenuItem_ResetOpacity(object sender, RoutedEventArgs e)
    {
        SetOpacityLevel(GeneralSettings.DefaultOpacityLevel);
    }

    // Called from the EventSub thread: the chat and the page are only touched on the UI thread
    private void PushNewChatMessageDispatcherInvoke(string message, string nick, string color)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (this.webView?.CoreWebView2 == null)
                return;

            string js = this._currentChat.PushNewChatMessage(message, nick, color);
            if (!string.IsNullOrEmpty(js))
                _ = this.webView.ExecuteScriptAsync(js);
        });
    }

    // "Sempre no topo" of the tray menu
    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        ToggleAlwaysOnTop();
    }

    // "Topo" of the side toolbar (also switched by UI Automation, which raises no Click)
    private void btnQuickTop_Toggled(object sender, RoutedEventArgs e)
    {
        if (btnQuickTop.IsChecked != AlwaysOnTop)
            ToggleAlwaysOnTop();
    }

    private void MenuItem_ToggleInteractable(object sender, RoutedEventArgs e)
    {
        ToggleInteractable();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        if (!App.IsShuttingDown)
            ExitApplication();
    }
}