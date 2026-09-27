namespace OlhoNoChat;

using Microsoft.Web.WebView2.Core;
using System.Windows;

/// <summary>
/// Side toolbar actions and tooltips, and the messages posted by our scripts in the chat page.
/// </summary>
public partial class MainWindow
{
    private const string ExitScrollModeMessage = "onc:exit-scroll-mode";
    private const string PlaySoundMessage = "onc:play-sound";

    // Set when the scroll mode is started from the toolbar, so that leaving it shows how to
    // bring the borders back (the user came from the bordered window).
    private bool _scrollModeFromToolbar = false;

    // "Rolar": the chat already scrolls while the borders are shown, so this hides the borders
    // and keeps the chat scrollable on top of the game.
    private void QuickScrollMode_Click(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;

        // The scroll-mode banner already explains how to leave; the borders notice comes after.
        _suppressBordersHintOnce = true;
        hideBorders();

        _scrollModeFromToolbar = true;
        SetInteractable(true);
    }

    // Tooltips of the title bar and side toolbar buttons, with the current keyboard shortcut.
    private void UpdateHotkeyTooltips()
    {
        var settings = App.Settings.GeneralSettings;

        this.btnHide.ToolTip = WithHotkey(
            "Deixa só o chat por cima do jogo. Para mostrar as bordas de novo, use o atalho ou o ícone do Olho no Chat perto do relógio.",
            settings.ToggleBordersHotkey);
        this.btnQuickTop.ToolTip = WithHotkey(settings.AlwaysOnTop
            ? "Sempre no topo: ligado. O chat fica na frente do jogo e das outras janelas. Clique para desligar."
            : "Sempre no topo: desligado. O chat é uma janela comum, que fica atrás de outra quando você clica nela. Clique para ligar.",
            settings.BringToTopHotkey);
        this.btnQuickScroll.ToolTip = WithHotkey(
            "Modo rolagem: esconde as bordas e deixa rolar o chat com a rodinha do mouse.", settings.ToggleInteractableHotkey);
    }

    // Arrows of the side toolbar in a short window: one button per step (held: keeps scrolling)
    private void QuickToolbarArrow_Click(object sender, RoutedEventArgs e)
    {
        var arrow = (FrameworkElement)sender;
        var scroll = (System.Windows.Controls.ScrollViewer)arrow.TemplatedParent;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + int.Parse((string)arrow.Tag) * 41);
    }

    private static string WithHotkey(string text, Hotkey hotkey)
    {
        return hotkey != null && hotkey.Key != System.Windows.Input.Key.None
            ? $"{text}\nAtalho: {hotkey}"
            : text;
    }

    // Messages posted by our scripts in the chat page (e.g. a message that may ring, clicking the scroll-mode banner).
    private void webView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return; // not a plain string message
        }

        if (message == PlaySoundMessage)
        {
            _chatSound.Play();
            return;
        }

        if (TryHandleComposeMessage(message) || TryHandleChatStateMessage(message))
            return;

        if (message == ExitScrollModeMessage && this.webView.Focusable)
            SetInteractable(false);
    }
}
