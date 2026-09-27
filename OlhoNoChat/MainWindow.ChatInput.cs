namespace OlhoNoChat;

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NHotkey;
using OlhoNoChat.Twitch;

/// <summary>
/// Writing in the chats of a channel ("Padrão" and "Chat oficial da Twitch"; "Endereço personalizado"
/// has no known channel). Two typing boxes, one at a time (option in the Twitch tab):
/// - the app's "Escrever no chat…" box under the chat, sending as the account connected in the Twitch
///   tab (through the Twitch API, see TwitchAccount);
/// - only in the "Chat oficial da Twitch": Twitch's own box inside the page (emotes, replies, commands),
///   which needs a login to twitch.tv inside the chat window.
/// The box starts closed. "Escrever" (chat bubble of the title bar) opens it (borders visible); the hotkey opens it
/// on top of the chat in the game too. Enter sends and the box stays open (option "Fechar a caixa depois
/// de enviar" in the Twitch tab: in the game it closes and the game comes back); "Escrever" again, the
/// hotkey again, Esc or its "×" close it, and in the game a click on the game too.
/// </summary>
public partial class MainWindow
{
    private const string ComposeSentMessage = "onc:compose-sent";
    private const string ComposeCancelMessage = "onc:compose-cancel";

    private bool _composing = false; // opened with the hotkey
    private bool _chatBoxOpen = false; // opened with "Escrever" (borders visible)
    private bool _composeFromButton = false; // Twitch's own box opened with "Escrever" (borders visible)
    private bool _composingInTwitchBox = false;
    private bool _webViewFocusableBeforeCompose = false;
    private IntPtr _composeReturnWindow = IntPtr.Zero;
    private bool _sendingChatMessage = false;
    private Task<bool> _twitchAccountCheck;

    private void StartChatInput()
    {
        _twitchAccount.Changed += () => Dispatcher.BeginInvoke(new Action(OnTwitchAccountChanged));

        // Clicking the game (or anything else) while writing closes the box
        this.Deactivated += (s, e) => { if (_composing && !_composeFromButton) EndCompose(returnFocus: false); };

        // The box's own "x" only shows while it has the focus and some text (its template can be made again later)
        tbChatMessage.GotKeyboardFocus += (s, e) => { HideMessageBoxClearButton(); PreloadEmotes(); };
        tbChatMessage.TextChanged += (s, e) => HideMessageBoxClearButton();

        UpdateChatInput();
        _ = CheckTwitchAccountOnceAsync();
    }

    // The saved access is checked with Twitch once per run (it may have expired or been removed)
    private Task<bool> CheckTwitchAccountOnceAsync() => _twitchAccountCheck ??= _twitchAccount.CheckAsync();

    private async Task StartRedemptionsAsync()
    {
        if (App.Settings.GeneralSettings.RedemptionsEnabled && await CheckTwitchAccountOnceAsync()
            && App.Settings.GeneralSettings.RedemptionsEnabled)
        {
            await EnsureTwitchService().InitializeAsync();
        }
    }

    // The redemptions client is only built when redemptions are used (they are off by default)
    private TwitchService EnsureTwitchService()
    {
        if (_twitchService == null)
        {
            _twitchService = new TwitchService(_serviceProvider.GetRequiredService<ILoggerFactory>(), _twitchAccount);
            _twitchService.ChannelPointsRewardRedeemed += OnChannelPointsRewardRedeemed;
        }
        return _twitchService;
    }

    private void OnTwitchAccountChanged()
    {
        if (!_twitchAccount.IsConnected)
        {
            _twitchService?.DisableEventSub();
            EndCompose(returnFocus: false);
            _emoteGroups = null; // frees the pictures (another account loads its own list, see EmotesLoaded)
        }
        else if (App.Settings.GeneralSettings.RedemptionsEnabled)
        {
            _ = EnsureTwitchService().InitializeAsync();
        }

        UpdateChatInput();
    }

    // After switching (in Configurações) from "Endereço personalizado" to a chat where one can write,
    // a notice on the chat tells how to open the box in the game. Shown once the settings window is closed
    // and the chat has loaded.
    private bool _writeHintPending = false;

    private void RequestWriteHint()
    {
        _writeHintPending = true;
        TryShowWriteHint();
    }

    private void TryShowWriteHint()
    {
        bool chatPageLoaded = _chatPageLoadedAt != DateTime.MinValue && !_chatNavigationPending;
        if (!_writeHintPending || _settingsDialogOpen || !chatPageLoaded || this.webView?.CoreWebView2 == null)
            return;

        _writeHintPending = false;
        if (ChatTypeUsesChannel && SavedChannel.Length > 0)
            ShowChatToast(WriteHintText());
    }

    private string WriteHintText()
    {
        Hotkey hotkey = App.Settings.GeneralSettings.WriteMessageHotkey;
        string inGame = hotkey != null && hotkey.Key != Key.None
            ? $"No jogo, aperte {hotkey} para abrir ou fechar a caixa de escrever."
            : "Para abrir a caixa no jogo, escolha um atalho em Configurações > Geral.";

        string button = _hiddenBorders ? string.Empty : "Clique no balão de conversa, na barra laranja, para abrir a caixa. ";
        return _twitchAccount.IsConnected || UseTwitchChatBox
            ? "Neste chat você pode escrever. " + button + inGame
            : "Neste chat você pode escrever depois de conectar sua conta em Configurações > Twitch. " + inGame;
    }

    // Channel of the chat shown (the messages go to it)
    private static string ChatChannel => ChatTypeUsesChannel ? SavedChannel : string.Empty;

    // Twitch's own box chosen in the Twitch tab (the "Padrão" page has none)
    private static bool UseTwitchChatBox => App.Settings.GeneralSettings.ChatType == (int)ChatTypes.TwitchPopout
                                            && App.Settings.GeneralSettings.UseTwitchChatBox;

    // The app's box can be used
    private bool ChatInputAvailable => !UseTwitchChatBox && _twitchAccount.IsConnected && _twitchAccount.CanSendMessages
                                       && ChatChannel.Length > 0;

    // Twitch's own box can be used: its chat page is the one loaded (not the welcome page)
    private bool TwitchBoxAvailable => UseTwitchChatBox && ChatChannel.Length > 0 && _currentChat?.ChatType == ChatTypes.TwitchPopout;

    private bool ChatBoxOpen => _composing || (_chatBoxOpen && !_hiddenBorders);

    private void UpdateChatInput()
    {
        if (!ChatInputAvailable)
            _chatBoxOpen = false;

        bool show = ChatInputAvailable && ChatBoxOpen;
        MessageBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateResizeCorner(); // the box holds the corner while it is open
        if (!show)
            EmotePopup.IsOpen = false;

        if (show)
            tbChatMessage.ToolTip = $"Vai para o chat de {ChatChannel} como {_twitchAccount.DisplayName}";

        Hotkey hotkey = App.Settings.GeneralSettings.WriteMessageHotkey;
        btnCloseChatBox.ToolTip = WithHotkey(_composing ? "Fechar a caixa e voltar para o jogo (Esc)." : "Fechar a caixa (Esc).", hotkey);

        // "Escrever" is lit while the box is open, and only offered where one can write
        btnQuickWrite.Visibility = ChatTypeUsesChannel ? Visibility.Visible : Visibility.Collapsed;
        btnQuickWrite.IsChecked = ChatBoxOpen;
        btnQuickWrite.ToolTip = WithHotkey(ChatBoxOpen ? "Fechar a caixa de escrever no chat." : "Escrever no chat: abre a caixa embaixo do chat.", hotkey);
    }

    // "Escrever" of the title bar (also switched by UI Automation, which raises no Click)
    private void btnQuickWrite_Toggled(object sender, RoutedEventArgs e)
    {
        if (btnQuickWrite.IsChecked == ChatBoxOpen)
            return;

        if (ChatBoxOpen)
            CloseChatBox(returnFocus: false);
        else if (TwitchBoxAvailable)
        {
            _composeFromButton = true; // stays open like the app's box (see Deactivated, hideBorders)
            StartCompose();
        }
        else if (!ChatInputAvailable)
        {
            btnQuickWrite.IsChecked = false;
            ShowWriteUnavailableHint();
        }
        else
        {
            _chatBoxOpen = true;
            UpdateChatInput();
            FocusMessageBox();
        }
    }

    // "×", Esc, "Escrever" or the hotkey
    private void CloseChatBox(bool returnFocus)
    {
        EmotePopup.IsOpen = false;
        ShowChatInputStatus(null);
        _chatBoxOpen = false;

        if (_composing)
            EndCompose(returnFocus);
        else
            UpdateChatInput();
    }

    private void btnCloseChatBox_Click(object sender, RoutedEventArgs e)
    {
        CloseChatBox(returnFocus: true);
    }

    private static bool CloseChatBoxAfterSend => App.Settings.GeneralSettings.CloseChatBoxAfterSend;

    // The chat page and its dark background also cover the message box row when the borders are hidden
    private void SetChatRowSpan(int rows)
    {
        this.webView?.SetValue(Grid.RowSpanProperty, rows);
        this.overlay.SetValue(Grid.RowSpanProperty, rows);
    }

    private void OnHotKeyWriteMessage(object sender, HotkeyEventArgs e)
    {
        e.Handled = true;
        if (!hasWebView2Runtime) return;

        // Pressed again closes the box; a box opened with "Escrever" while the game is in front gets the focus instead
        if (_composing || (_chatBoxOpen && !_hiddenBorders && this.IsActive))
        {
            CloseChatBox(returnFocus: true);
            return;
        }

        if (!ChatInputAvailable && !TwitchBoxAvailable)
        {
            ShowWriteUnavailableHint();
            return;
        }

        _composeFromButton = false;
        StartCompose();
    }

    private void ShowWriteUnavailableHint()
    {
        ShowChatToast(!ChatTypeUsesChannel
            ? "Para escrever no chat, escolha o tipo de chat \"Padrão\" ou \"Chat oficial da Twitch\" em Configurações > Chat."
            : SavedChannel.Length == 0
                ? "Para escrever no chat, escolha o canal na faixa de cima do chat."
                : !_twitchAccount.IsConnected
                    ? "Para escrever no chat, conecte sua conta da Twitch em Configurações > Twitch."
                    : "Para escrever no chat, conecte sua conta de novo em Configurações > Twitch: a Twitch precisa dar a permissão de escrever.");
    }

    private void StartCompose()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        IntPtr foreground = WindowHelper.GetForegroundWindow();
        _composeReturnWindow = foreground != hwnd ? foreground : IntPtr.Zero;
        _composing = true;
        _composingInTwitchBox = UseTwitchChatBox;

        // The window takes clicks and keys while the box is open
        if (_hiddenBorders)
            WindowHelper.SetWindowExDefault(hwnd);

        if (_composingInTwitchBox)
        {
            _webViewFocusableBeforeCompose = this.webView.Focusable;
            this.webView.Focusable = true;
        }
        else if (_hiddenBorders)
        {
            SetChatRowSpan(1); // room for the box under the chat
        }

        UpdateChatInput();

        ActivateChatWindow();

        if (_composingInTwitchBox)
        {
            this.webView.Focus();
            _ = this.webView.CoreWebView2?.ExecuteScriptAsync(
                "(function () { document.body.classList.add('onc-writing'); var box = document.querySelector('[data-a-target=\"chat-input\"]'); if (box) box.focus(); })();");
        }
        else
        {
            tbChatMessage.Focus();
            Keyboard.Focus(tbChatMessage);
        }
    }

    // Twitch's box tells when its message was sent or Esc was pressed (see CustomURLChat.SetupJavascript)
    private bool TryHandleComposeMessage(string message)
    {
        if (message != ComposeSentMessage && message != ComposeCancelMessage)
            return false;

        if (_composingInTwitchBox && (message == ComposeCancelMessage || (CloseChatBoxAfterSend && !_composeFromButton)))
            EndCompose(returnFocus: true);
        return true;
    }

    private void EndCompose(bool returnFocus)
    {
        if (!_composing)
            return;

        _composing = false;
        _composeFromButton = false;

        if (_composingInTwitchBox)
        {
            this.webView.Focusable = _webViewFocusableBeforeCompose;
            _ = this.webView.CoreWebView2?.ExecuteScriptAsync("document.body && document.body.classList.remove('onc-writing');");
        }
        _composingInTwitchBox = false;

        if (_hiddenBorders)
        {
            SetChatRowSpan(2);
            if (CurrentDisplayMode == WindowDisplayMode.Overlay)
                WindowHelper.SetWindowExTransparent(new WindowInteropHelper(this).Handle);
        }

        UpdateChatInput();

        if (returnFocus && _composeReturnWindow != IntPtr.Zero)
            WindowHelper.SetForegroundWindow(_composeReturnWindow);
        _composeReturnWindow = IntPtr.Zero;
    }

    private void tbChatMessage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = SendChatMessageAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (EmotePopup.IsOpen)
            {
                EmotePopup.IsOpen = false; // Esc closes the emote list first
                return;
            }
            CloseChatBox(returnFocus: true);
        }
    }

    private void tbChatMessage_TextChanged(object sender, TextChangedEventArgs e)
    {
        ShowChatInputStatus(null);
    }

    private void btnSendChatMessage_Click(object sender, RoutedEventArgs e)
    {
        _ = SendChatMessageAsync();
    }

    private async Task SendChatMessageAsync()
    {
        string text = tbChatMessage.Text.Trim();
        if (text.Length == 0 || _sendingChatMessage)
            return;

        _sendingChatMessage = true;
        btnSendChatMessage.IsEnabled = false;
        TwitchAccount.SendResult result;
        try
        {
            result = await _twitchAccount.SendChatMessageAsync(ChatChannel, text);
        }
        finally
        {
            _sendingChatMessage = false;
            btnSendChatMessage.IsEnabled = true;
        }

        if (result.Status == TwitchAccount.SendStatus.Sent)
        {
            tbChatMessage.Clear();
            if (_composing && CloseChatBoxAfterSend)
                CloseChatBox(returnFocus: true);
            else
                FocusMessageBox(); // ready for the next message
        }
        else
        {
            ShowChatInputStatus(result.Message);
        }
    }

    private void ShowChatInputStatus(string message)
    {
        tbChatMessageStatus.Text = message ?? string.Empty;
        tbChatMessageStatus.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowChatToast(string text)
    {
        if (this.webView?.CoreWebView2 != null)
            _ = this.webView.CoreWebView2.ExecuteScriptAsync($"{ShowToastScript}({JsonSerializer.Serialize(text)});");
    }
}
