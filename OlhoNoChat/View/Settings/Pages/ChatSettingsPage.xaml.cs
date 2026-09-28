using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Chat" tab: chat type, old messages and filters (the channel is changed in the strip above the chat). Theme and text options are on the
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
        this.cbHideBots.IsOn = App.Settings.GeneralSettings.BlockBotActivity;
        this.cbHideGifs.IsOn = App.Settings.GeneralSettings.HideGifs;
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
                App.Settings.GeneralSettings.BetterTtv = this.cbBetterTtv.IsOn;
                App.Settings.GeneralSettings.BetterTtv_7tv = this.cbBetterTtv_7tv.IsOn;
                App.Settings.GeneralSettings.BetterTtv_AdvEmoteMenu = this.cbBetterTtv_AdvMenu.IsOn;
                App.Settings.GeneralSettings.FrankerFaceZ = this.cbFfz.IsOn;
            }
            else if (chatType == ChatTypes.Padrao)
            {
                App.Settings.GeneralSettings.CustomURL = string.Empty;
                // RedemptionsEnabled is saved by the Twitch tab (ConnectionSettingsPage)
                App.Settings.GeneralSettings.BlockBotActivity = this.cbHideBots.IsOn;
                App.Settings.GeneralSettings.HideGifs = this.cbHideGifs.IsOn;
                App.Settings.GeneralSettings.FadeChat = this.cbFade.IsOn;
                App.Settings.GeneralSettings.FadeTime = this.tbFadeTime.Text;
            }
        }
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
