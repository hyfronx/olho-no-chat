using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Geral" tab: chat window behaviour, hotkeys, updates and advanced options.
/// Sound options are on the "Som" tab (SoundSettingsPage).
/// </summary>
public partial class GeneralSettingsPage : UserControl
{
    public event Action CheckForUpdateRequested;
    public event Action ResetToDefaultsRequested;

    public GeneralSettingsPage()
    {
        InitializeComponent();
    }

    public void SetupValues()
    {
        this.cbAutoHideBorders.IsOn = App.Settings.GeneralSettings.AutoHideBorders;
        this.cbTaskbar.IsOn = App.Settings.GeneralSettings.HideTaskbarIcon;
        this.cbInteraction.IsOn = App.Settings.GeneralSettings.AllowInteraction;
        this.cbCheckForUpdates.IsOn = App.Settings.GeneralSettings.CheckForUpdates;
        this.cbMultiInstance.IsOn = App.Settings.GeneralSettings.AllowMultipleInstances;

        this.hotkeyInputToggleBorders.Hotkey = App.Settings.GeneralSettings.ToggleBordersHotkey;
        this.hotkeyInputToggleInteractable.Hotkey = App.Settings.GeneralSettings.ToggleInteractableHotkey;
        this.hotkeyInputBringToTop.Hotkey = App.Settings.GeneralSettings.BringToTopHotkey;
        this.hotkeyInputWriteMessage.Hotkey = App.Settings.GeneralSettings.WriteMessageHotkey;
    }

    public void SaveValues()
    {
        App.Settings.GeneralSettings.AutoHideBorders = this.cbAutoHideBorders.IsOn;
        App.Settings.GeneralSettings.HideTaskbarIcon = this.cbTaskbar.IsOn;
        App.Settings.GeneralSettings.AllowInteraction = this.cbInteraction.IsOn;
        App.Settings.GeneralSettings.CheckForUpdates = this.cbCheckForUpdates.IsOn;
        App.Settings.GeneralSettings.AllowMultipleInstances = this.cbMultiInstance.IsOn;

        // Hotkeys
        App.Settings.GeneralSettings.ToggleBordersHotkey = hotkeyInputToggleBorders.Hotkey;
        App.Settings.GeneralSettings.ToggleInteractableHotkey = hotkeyInputToggleInteractable.Hotkey;
        App.Settings.GeneralSettings.BringToTopHotkey = hotkeyInputBringToTop.Hotkey;
        App.Settings.GeneralSettings.WriteMessageHotkey = hotkeyInputWriteMessage.Hotkey;
    }

    // "Mudar atalho" / "Confirmar" of a hotkey row (its editor is in the same row)
    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var editor = ((Grid)button.Parent).Children.OfType<HotkeyEditorControl>().Single();

        if (editor.IsCapturing)
        {
            editor.StopCapturing();
            button.Content = "Mudar atalho";
        }
        else
        {
            editor.StartCapturing();
            button.Content = "Confirmar";
        }
    }

    private void btnCheckForUpdatesNow_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdateRequested?.Invoke();
    }

    // The settings window asks first and then restores every tab (SettingsWindow.ResetToDefaultsAsync)
    private void btnResetToDefaults_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaultsRequested?.Invoke();
    }
}
