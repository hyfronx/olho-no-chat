using System.Windows;
using System.Windows.Controls;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Chat" tab: chat type, channel, old messages and filters. Theme and text options are on the
/// "Aparência" tab (AppearanceSettingsPage) and the new-message sound on "Som" (SoundSettingsPage).
/// </summary>
public partial class ChatSettingsPage : UserControl
{
    // The Chat Filters window saved (it saves on its own, see SettingsWindow.SettingsSaved)
    public event Action FiltersSaved;

    // Another chat type was picked (not saved yet): the other tabs show its options too
    public event Action<ChatTypes> ChatTypeSelectionChanged;

    public ChatSettingsPage()
    {
        InitializeComponent();
    }

    // "Tipo de chat": the position in the list is the ChatTypes value
    public int SelectedChatType
    {
        get => this.comboChatType.SelectedIndex;
        set => this.comboChatType.SelectedIndex = value;
    }

    public void SetupValues()
    {
        this.tbUsername.Text = App.Settings.GeneralSettings.Username;
        this.tbTwitchPopoutUsername.Text = App.Settings.GeneralSettings.Username;
        this.cbHideBots.IsOn = App.Settings.GeneralSettings.BlockBotActivity;
        this.cbFade.IsOn = App.Settings.GeneralSettings.FadeChat;
        this.tbFadeTime.Text = App.Settings.GeneralSettings.FadeTime;
        this.fadeTimeRow.IsEnabled = App.Settings.GeneralSettings.FadeChat;

        this.cbBetterTtv.IsOn = App.Settings.GeneralSettings.BetterTtv;
        this.cbBetterTtv_7tv.IsOn = App.Settings.GeneralSettings.BetterTtv_7tv;
        this.cbBetterTtv_AdvMenu.IsOn = App.Settings.GeneralSettings.BetterTtv_AdvEmoteMenu;
        this.cbFfz.IsOn = App.Settings.GeneralSettings.FrankerFaceZ;

        if (Enum.IsDefined(typeof(ChatTypes), App.Settings.GeneralSettings.ChatType))
        {
            var chatType = (ChatTypes)App.Settings.GeneralSettings.ChatType;

            // Empty for the other types (it used to keep "url", which was then opened as the address)
            this.tbURL.Text = chatType == ChatTypes.CustomURL ? App.Settings.GeneralSettings.CustomURL : string.Empty;

            ShowPanelFor(chatType);
        }
    }

    public void SaveValues()
    {
        if (Enum.IsDefined(typeof(ChatTypes), App.Settings.GeneralSettings.ChatType))
        {
            var chatType = (ChatTypes)App.Settings.GeneralSettings.ChatType;

            if (chatType == ChatTypes.CustomURL)
            {
                App.Settings.GeneralSettings.CustomURL = this.tbURL.Text;
            }
            else if (chatType == ChatTypes.TwitchPopout)
            {
                // Empty: no channel (the chat shows the welcome page)
                App.Settings.GeneralSettings.Username = ReadChannel(this.tbTwitchPopoutUsername);

                App.Settings.GeneralSettings.BetterTtv = this.cbBetterTtv.IsOn;
                App.Settings.GeneralSettings.BetterTtv_7tv = this.cbBetterTtv_7tv.IsOn;
                App.Settings.GeneralSettings.BetterTtv_AdvEmoteMenu = this.cbBetterTtv_AdvMenu.IsOn;
                App.Settings.GeneralSettings.FrankerFaceZ = this.cbFfz.IsOn;
            }
            else if (chatType == ChatTypes.Padrao)
            {
                App.Settings.GeneralSettings.CustomURL = string.Empty;
                App.Settings.GeneralSettings.Username = ReadChannel(this.tbUsername);
                // RedemptionsEnabled is saved by the Twitch tab (ConnectionSettingsPage)
                App.Settings.GeneralSettings.BlockBotActivity = this.cbHideBots.IsOn;
                App.Settings.GeneralSettings.FadeChat = this.cbFade.IsOn;
                App.Settings.GeneralSettings.FadeTime = this.tbFadeTime.Text;
            }
        }
    }

    /// <summary>
    /// Checks the channel of the chosen chat type before saving ("nome", "@nome" or the link are fine).
    /// Returns false, and shows why under the box, when it is not a possible Twitch channel name.
    /// </summary>
    public bool ValidateValues(ChatTypes chatType)
    {
        var (box, error) = chatType switch
        {
            ChatTypes.Padrao => (this.tbUsername, this.tbUsernameError),
            ChatTypes.TwitchPopout => (this.tbTwitchPopoutUsername, this.tbTwitchPopoutUsernameError),
            _ => (null, null)
        };
        if (box == null)
            return true;

        string name = TwitchNames.Extract(box.Text);
        bool valid = name.Length == 0 || TwitchNames.IsValid(name);
        error.Text = valid ? string.Empty : TwitchNames.InvalidNameHint;
        error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (!valid)
            box.Focus();
        return valid;
    }

    // The channel name as it is saved (lower case, without "@" or the link around it); the box shows it too
    private static string ReadChannel(TextBox box)
    {
        string name = TwitchNames.Extract(box.Text).ToLowerInvariant();
        box.Text = name;
        return name;
    }

    private void ChannelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Also called while the page is still being created (the boxes get their first text)
        if (this.tbUsernameError != null)
            this.tbUsernameError.Visibility = Visibility.Collapsed;
        if (this.tbTwitchPopoutUsernameError != null)
            this.tbTwitchPopoutUsernameError.Visibility = Visibility.Collapsed;
    }

    // Only the options of the chosen chat type are shown
    private void ShowPanelFor(ChatTypes chatType)
    {
        this.padraoGrid.Visibility = chatType == ChatTypes.Padrao ? Visibility.Visible : Visibility.Collapsed;
        this.twitchPopoutChat.Visibility = chatType == ChatTypes.TwitchPopout ? Visibility.Visible : Visibility.Collapsed;
        this.customURLGrid.Visibility = chatType == ChatTypes.CustomURL ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Event Handlers --------------------------------------------------------------------------------

    private void comboChatType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only the pages change here; the setting itself is written when saving
        if (!Enum.IsDefined(typeof(ChatTypes), this.comboChatType.SelectedIndex))
            return;

        var chatType = (ChatTypes)this.comboChatType.SelectedIndex;
        ShowPanelFor(chatType);
        ChatTypeSelectionChanged?.Invoke(chatType);
    }

    private void cbFade_Toggled(object sender, RoutedEventArgs e)
    {
        this.fadeTimeRow.IsEnabled = this.cbFade.IsOn;
    }

    private void btOpenChatFilterSettings_Click(object sender, RoutedEventArgs e)
    {
        ChatFilters chatFiltersWindow = new ChatFilters();
        chatFiltersWindow.Owner = Window.GetWindow(this);
        chatFiltersWindow.Saved += () => FiltersSaved?.Invoke();
        chatFiltersWindow.ShowDialog();
    }
}
