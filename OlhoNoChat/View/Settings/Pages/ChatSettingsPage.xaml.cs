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
        this.cbHideBots.IsOn = App.Opcoes.EsconderBots;
        this.cbHideGifs.IsOn = App.Opcoes.EsconderGifs;
        this.cbHideOtherChannels.IsOn = App.Opcoes.EsconderOutrosCanais;
        this.cbFade.IsOn = App.Opcoes.ApagarMensagensAntigas;
        this.tbFadeTime.Text = App.Opcoes.SegundosParaApagar;
        this.fadeTimeRow.IsEnabled = App.Opcoes.ApagarMensagensAntigas;

        this.cbBetterTtv.IsOn = App.Opcoes.BetterTtv;
        this.cbBetterTtv_7tv.IsOn = App.Opcoes.Emotes7tv;
        this.cbBetterTtv_AdvMenu.IsOn = App.Opcoes.MenuDeEmotesDoBetterTtv;
        this.cbFfz.IsOn = App.Opcoes.FrankerFaceZ;

        if (Enum.IsDefined(typeof(ChatTypes), App.Opcoes.TipoDeChat))
        {
            var chatType = (ChatTypes)App.Opcoes.TipoDeChat;

            // Empty for the other types (it used to keep "url", which was then opened as the address)
            this.tbURL.Text = chatType == ChatTypes.CustomURL ? App.Opcoes.EnderecoPersonalizado : string.Empty;

            ShowPanelFor(chatType);
        }
    }

    public void SaveValues()
    {
        if (Enum.IsDefined(typeof(ChatTypes), App.Opcoes.TipoDeChat))
        {
            var chatType = (ChatTypes)App.Opcoes.TipoDeChat;

            if (chatType == ChatTypes.CustomURL)
            {
                App.Opcoes.EnderecoPersonalizado = this.tbURL.Text;
            }
            else if (chatType == ChatTypes.TwitchPopout)
            {
                App.Opcoes.BetterTtv = this.cbBetterTtv.IsOn;
                App.Opcoes.Emotes7tv = this.cbBetterTtv_7tv.IsOn;
                App.Opcoes.MenuDeEmotesDoBetterTtv = this.cbBetterTtv_AdvMenu.IsOn;
                App.Opcoes.FrankerFaceZ = this.cbFfz.IsOn;
            }
            else if (chatType == ChatTypes.Padrao)
            {
                App.Opcoes.EnderecoPersonalizado = string.Empty;
                // RedemptionsEnabled is saved by the Twitch tab (ConnectionSettingsPage)
                App.Opcoes.EsconderBots = this.cbHideBots.IsOn;
                App.Opcoes.EsconderGifs = this.cbHideGifs.IsOn;
                App.Opcoes.EsconderOutrosCanais = this.cbHideOtherChannels.IsOn;
                App.Opcoes.ApagarMensagensAntigas = this.cbFade.IsOn;
                App.Opcoes.SegundosParaApagar = this.tbFadeTime.Text;
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
