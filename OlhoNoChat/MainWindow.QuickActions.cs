namespace OlhoNoChat;

using OlhoNoChat.Atalhos;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// Quick settings of the title bar (text size and background panels with a slider, tooltips), and the
/// messages posted by our scripts in the chat page.
/// </summary>
public partial class MainWindow
{
    private const string ExitScrollModeMessage = "onc:exit-scroll-mode";
    private const string PlaySoundMessage = "onc:play-sound";

    // Steps of the mouse wheel over the text size and background buttons (the sliders move in smaller steps)
    private const double TextSizeWheelStep = 0.1;
    private const double BackgroundWheelStep = 5; // percent

    // Tooltips of the title bar buttons, with the current keyboard shortcut.
    private void UpdateHotkeyTooltips()
    {
        var settings = App.Settings.GeneralSettings;

        this.btnHide.ToolTip = WithHotkey(
            "Ocultar bordas: deixa só o chat por cima do jogo. Para mostrar as bordas de novo, use o atalho ou o ícone do Olho no Chat perto do relógio.",
            settings.ToggleBordersHotkey);
        this.btnQuickTop.ToolTip = WithHotkey(settings.AlwaysOnTop
            ? "Sempre no topo: ligado. O chat fica na frente do jogo e das outras janelas. Clique para desligar."
            : "Sempre no topo: desligado. O chat é uma janela comum, que fica atrás de outra quando você clica nela. Clique para ligar.",
            settings.BringToTopHotkey);
    }

    private bool _syncingQuickSliders = false;
    private Popup _quickPanelClosed;
    private DateTime _quickPanelClosedAt = DateTime.MinValue;
    private DispatcherTimer _persistTimer;

    // Current text size and background in the panels and the tooltips of their buttons
    private void UpdateQuickValues()
    {
        var settings = App.Settings.GeneralSettings;
        string textSize = $"{Math.Round(settings.ZoomLevel * 100)}%";
        string background = $"{Math.Round(settings.OpacityLevel / 2.55)}%";

        _syncingQuickSliders = true;
        this.sliderTextSize.Value = settings.ZoomLevel;
        this.sliderBackground.Value = settings.OpacityLevel / 2.55;
        _syncingQuickSliders = false;

        this.tbTextSizeValue.Text = textSize;
        this.tbBackgroundValue.Text = background;
        System.Windows.Automation.AutomationProperties.SetHelpText(this.btnTextSize, textSize);
        System.Windows.Automation.AutomationProperties.SetHelpText(this.btnBackground, background);
        this.tipTextSize.Content = $"Tamanho do texto: {textSize}\nClique para ajustar, ou gire a rodinha do mouse aqui.";
        this.tipBackground.Content = $"Fundo do chat: {background}\nClique para ajustar, ou gire a rodinha do mouse aqui.";
        this.btnTextSizeReset.ToolTip = $"Voltar ao padrão ({Math.Round(GeneralSettings.DefaultZoomLevel * 100)}%)";
        this.btnBackgroundReset.ToolTip = $"Voltar ao padrão ({Math.Round(GeneralSettings.DefaultOpacityLevel / 2.55)}%)";
    }

    // Saved a moment after the last change (a slider sends many while it is dragged): otherwise
    // the change is lost if the app does not close normally
    private void SchedulePersist()
    {
        if (_persistTimer == null)
        {
            _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _persistTimer.Tick += (s, e) =>
            {
                _persistTimer.Stop();
                App.Settings.Persist();
            };
        }

        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void sliderTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingQuickSliders || !hasWebView2Runtime) return;
        SetZoomFactor(e.NewValue);
        SchedulePersist();
    }

    private void sliderBackground_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingQuickSliders) return;
        SetOpacityLevel((int)Math.Round(e.NewValue * 2.55));
    }

    private void TextSizeReset_Click(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;
        SetZoomFactor(GeneralSettings.DefaultZoomLevel);
        SchedulePersist();
    }

    private void BackgroundReset_Click(object sender, RoutedEventArgs e)
    {
        SetOpacityLevel(GeneralSettings.DefaultOpacityLevel);
    }

    // The mouse wheel over the button (or over the slider) changes the value without opening the panel
    private void TextSize_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (!hasWebView2Runtime) return;
        SetZoomFactor(App.Settings.GeneralSettings.ZoomLevel + Math.Sign(e.Delta) * TextSizeWheelStep);
        SchedulePersist();
        ShowValueTip(sender, this.tipTextSize);
    }

    private void Background_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        double percent = Math.Round(App.Settings.GeneralSettings.OpacityLevel / 2.55) + Math.Sign(e.Delta) * BackgroundWheelStep;
        SetOpacityLevel((int)Math.Round(Math.Clamp(percent, 0, 100) * 2.55));
        ShowValueTip(sender, this.tipBackground);
    }

    // The tooltip shows the new value while the wheel turns over the button (it closes when the mouse leaves)
    private static void ShowValueTip(object sender, ToolTip tip)
    {
        if (sender is Button)
            tip.IsOpen = true;
    }

    private void QuickPanelButton_MouseLeave(object sender, MouseEventArgs e)
    {
        this.tipTextSize.IsOpen = false;
        this.tipBackground.IsOpen = false;
    }

    private Popup QuickPanelOf(object button) => button == this.btnTextSize ? this.TextSizePanel : this.BackgroundPanel;

    private void QuickPanelButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // With its panel open, the button closes it
        Popup panel = QuickPanelOf(sender);
        if (panel.IsOpen)
        {
            e.Handled = true;
            panel.IsOpen = false;
        }
    }

    private void QuickPanelButton_Click(object sender, RoutedEventArgs e)
    {
        Popup panel = QuickPanelOf(sender);

        // The click that closed the panel (outside it) must not open it again
        if (panel == _quickPanelClosed && DateTime.UtcNow - _quickPanelClosedAt < TimeSpan.FromMilliseconds(300))
            return;

        panel.IsOpen = true;

        // The arrow keys move the slider right away
        Slider slider = panel == this.TextSizePanel ? this.sliderTextSize : this.sliderBackground;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(slider));
    }

    // Watched as soon as a panel starts closing: its Closed event comes only after the fade, and the click
    // that closed it would open it again
    private void SetupQuickPanels()
    {
        var isOpen = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Popup.IsOpenProperty, typeof(Popup));
        foreach (Popup panel in new[] { this.TextSizePanel, this.BackgroundPanel })
        {
            isOpen.AddValueChanged(panel, (s, e) =>
            {
                ((FrameworkElement)panel.PlacementTarget).Tag = panel.IsOpen ? "open" : null;
                if (!panel.IsOpen)
                {
                    _quickPanelClosed = panel;
                    _quickPanelClosedAt = DateTime.UtcNow;
                }
            });
        }
    }

    private void QuickPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            e.Handled = true;
            CloseQuickPanels();
        }
    }

    private void CloseQuickPanels()
    {
        this.TextSizePanel.IsOpen = false;
        this.BackgroundPanel.IsOpen = false;
    }

    private static string WithHotkey(string text, Atalho hotkey)
    {
        return Atalho.Existe(hotkey)
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
            _aviso.Tocar();
            return;
        }

        if (TryHandleComposeMessage(message) || TryHandleChatStateMessage(message))
            return;

        if (message == ExitScrollModeMessage && this.webView.Focusable)
            SetInteractable(false);
    }
}
