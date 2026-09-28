using OlhoNoChat.Atalhos;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OlhoNoChat.Helpers;
using OlhoNoChat.View.Settings;
using ContentDialog = ModernWpf.Controls.ContentDialog;
using ContentDialogButton = ModernWpf.Controls.ContentDialogButton;
using ContentDialogResult = ModernWpf.Controls.ContentDialogResult;
using FontIcon = ModernWpf.Controls.FontIcon;
using TextEditor = ICSharpCode.AvalonEdit.TextEditor;
using ToggleSwitch = ModernWpf.Controls.ToggleSwitch;

namespace OlhoNoChat;

/// <summary>
/// "Salvar" saves and applies right away and keeps the window open; "Fechar" closes it
/// (asking first if something was changed and not saved).
/// </summary>
public partial class SettingsWindow : Window
{
    public event Action CheckForUpdateRequested;

    // Raised after every save (also after saving in the Chat Filters window): the main window applies it
    public event Action SettingsSaved;

    private readonly ChatSettingsPage _chatSettingsPage;
    private readonly AppearanceSettingsPage _appearanceSettingsPage;
    private readonly SoundSettingsPage _soundSettingsPage;
    private readonly GeneralSettingsPage _generalSettingsPage;
    private readonly ConnectionSettingsPage _connectionSettingsPage;
    private readonly AboutSettingsPage _aboutSettingsPage;

    // What the pages showed when the window opened or was last saved, to know if something changed
    private string _savedState;
    private bool _closeConfirmed = false;
    private readonly DispatcherTimer _savedFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };

    public SettingsWindow(
        ConnectionSettingsPage connectionPage,
        ChatSettingsPage chatPage,
        AppearanceSettingsPage appearancePage,
        SoundSettingsPage soundPage,
        GeneralSettingsPage generalPage,
        AboutSettingsPage aboutPage)
    {
        InitializeComponent();

        _connectionSettingsPage = connectionPage;
        _chatSettingsPage = chatPage;
        _appearanceSettingsPage = appearancePage;
        _soundSettingsPage = soundPage;
        _generalSettingsPage = generalPage;
        _aboutSettingsPage = aboutPage;

        _generalSettingsPage.CheckForUpdateRequested += () => {
            // When the GeneralSettingsPage requests a check for updates, fire this window's own event.
            CheckForUpdateRequested?.Invoke();
        };
        _generalSettingsPage.ResetToDefaultsRequested += () => _ = ResetToDefaultsAsync();

        // The Chat Filters window saves on its own
        _chatSettingsPage.FiltersSaved += () => SettingsSaved?.Invoke();
        _chatSettingsPage.ChatTypeSelectionChanged += ChatTypeSelectionChanged;

        _savedFeedbackTimer.Tick += (s, e) => ShowSaveButtonNormal();

        // Set the initial page
        lvSettings.SelectedIndex = 0;
    }

    // The values shown by every option of the visible tabs (the chat type too), in a fixed order
    private string CurrentState()
    {
        var values = new List<string>();
        foreach (var page in new FrameworkElement[] { _chatSettingsPage, _appearanceSettingsPage, _soundSettingsPage, _generalSettingsPage, _connectionSettingsPage })
            CollectValues(page, values);
        return string.Join("\u001F", values);
    }

    private static void CollectValues(object element, List<string> values)
    {
        switch (element)
        {
            case TextBox textBox: values.Add(textBox.Text); return;
            case ToggleSwitch toggle: values.Add(toggle.IsOn.ToString()); return;
            case ComboBox combo: values.Add(combo.Items.Count + ":" + combo.SelectedIndex); return;
            case Slider slider: values.Add(Math.Round(slider.Value).ToString()); return;
            case TextEditor editor: values.Add(editor.Text); return;
            case TextBlock { Name: "tbSoundClipsFolder" } folder: values.Add(folder.Text); return;
        }

        if (element is DependencyObject parent)
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
                CollectValues(child, values);
    }

    private bool HasUnsavedChanges() => _savedState != null && CurrentState() != _savedState;

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        Save();
    }

    private void Save()
    {
        // The pages save according to the chosen chat type
        App.Settings.GeneralSettings.ChatType = _chatSettingsPage.SelectedChatType;

        _generalSettingsPage.SaveValues();
        _soundSettingsPage.SaveValues();
        _chatSettingsPage.SaveValues();
        _appearanceSettingsPage.SaveValues();
        _connectionSettingsPage.SaveValues();

        App.Settings.Persist();
        _savedState = CurrentState();

        SettingsSaved?.Invoke();
        ShowSaveButtonDone();
    }

    // "Restaurar tudo para o padrão" (Geral > Avançado): asks first, then restores the saved options
    // (AppSettings.ResetToDefaults), shows them on every tab and applies them to the chat like a save
    private async Task ResetToDefaultsAsync()
    {
        var dialog = new ContentDialog
        {
            Owner = this,
            Title = "Restaurar tudo para o padrão?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Todas as opções voltam como na primeira instalação (também o que você mudou e ainda não salvou). " +
                       "O canal, a conta da Twitch e as listas de nomes dos filtros continuam."
            },
            PrimaryButtonText = "Restaurar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch (InvalidOperationException)
        {
            // Another dialog is already open
            return;
        }
        if (result != ContentDialogResult.Primary)
            return;

        App.Settings.ResetToDefaults();

        SetupValues();
        _generalSettingsPage.SetupValues();
        _soundSettingsPage.SetupValues();
        _chatSettingsPage.SetupValues();
        _appearanceSettingsPage.SetupValues();
        _connectionSettingsPage.SetupValues();
        _ = Dispatcher.BeginInvoke(new Action(() => _savedState = CurrentState()), DispatcherPriority.ContextIdle);

        SettingsSaved?.Invoke();
        ShowSaveButtonDone();
    }

    // "Salvo" with a check mark for a moment after saving
    private void ShowSaveButtonDone()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = "Salvo", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        OKButton.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(OKButton, "Salvo");

        _savedFeedbackTimer.Stop();
        _savedFeedbackTimer.Start();
    }

    private void ShowSaveButtonNormal()
    {
        _savedFeedbackTimer.Stop();
        OKButton.Content = "Salvar";
        System.Windows.Automation.AutomationProperties.SetName(OKButton, "Salvar");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Esc closes the window. Handled before the focused control gets it: the toggle switches
    // would swallow it otherwise.
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && !EscapeBelongsToFocusedControl())
        {
            e.Handled = true;
            Close();
        }
    }

    // Esc closes an open list, or clears a shortcut box that is waiting for keys
    internal static bool EscapeBelongsToFocusedControl()
    {
        var element = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
        while (element != null)
        {
            switch (element)
            {
                case ComboBoxItem:
                case ComboBox { IsDropDownOpen: true }:
                case EditorDeAtalho { Gravando: true }:
                    return true;
            }

            // Lists show their items in a popup, outside the window's visual tree
            element = (element is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(element) : null)
                      ?? LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !HasUnsavedChanges())
            return;

        // Ask first, so changes are not lost by accident
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(AskToSaveBeforeClosing));
    }

    private async void AskToSaveBeforeClosing()
    {
        var dialog = new ContentDialog
        {
            Owner = this,
            Title = "Salvar as alterações?",
            Content = "Você mudou algumas opções e ainda não salvou.",
            PrimaryButtonText = "Salvar",
            SecondaryButtonText = "Não salvar",
            CloseButtonText = "Voltar",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch (InvalidOperationException)
        {
            // Another dialog is already open
            return;
        }

        if (result == ContentDialogResult.Primary)
            Save();
        else if (result != ContentDialogResult.Secondary)
            return; // "Voltar": keep editing

        _closeConfirmed = true;
        Close();
    }

    // The chat type first: picking it changes the other pages, whose SetupValues then show the saved values
    private void SetupValues()
    {
        _chatSettingsPage.SelectedChatType = App.Settings.GeneralSettings.ChatType;
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        SetupValues();

        _generalSettingsPage.SetupValues();
        _soundSettingsPage.SetupValues();
        _chatSettingsPage.SetupValues();
        _appearanceSettingsPage.SetupValues();
        _connectionSettingsPage.SetupValues();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Sistema.MolduraDaJanela.Aplicar(this); // own close button, no gray line around

        // Remember what the pages show once they have loaded the saved values
        Dispatcher.BeginInvoke(new Action(() => _savedState = CurrentState()), DispatcherPriority.ContextIdle);
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lvSettings.SelectedItem is ListViewItem selectedItem)
        {
            switch (selectedItem.Tag as string)
            {
                case "chat":
                    SettingsContentControl.Content = _chatSettingsPage;
                    break;
                case "appearance":
                    SettingsContentControl.Content = _appearanceSettingsPage;
                    break;
                case "sound":
                    SettingsContentControl.Content = _soundSettingsPage;
                    break;
                case "general":
                    SettingsContentControl.Content = _generalSettingsPage;
                    break;
                case "connections":
                    SettingsContentControl.Content = _connectionSettingsPage;
                    break;
                case "about":
                    SettingsContentControl.Content = _aboutSettingsPage;
                    break;
            }
        }
    }

    // "Tipo de chat" (Chat tab) changed: the other tabs show the options of that type
    private void ChatTypeSelectionChanged(ChatTypes chatType)
    {
        _appearanceSettingsPage.ChatTypeChanged(chatType);
        _soundSettingsPage.ChatTypeChanged(chatType);
    }
}
