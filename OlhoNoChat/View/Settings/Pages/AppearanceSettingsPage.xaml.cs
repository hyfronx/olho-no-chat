using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Aparência" tab: chat theme and message text options (or the CSS of the other chat types).
/// </summary>
public partial class AppearanceSettingsPage : UserControl
{
    public AppearanceSettingsPage()
    {
        InitializeComponent();

        tbPopoutCSS.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("CSS");
        tbCSS2.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("CSS");
    }

    public void SetupValues()
    {
        SelectByTag(this.comboTextColor, App.Settings.GeneralSettings.ChatMessageColor ?? string.Empty);
        SelectByTag(this.comboTextOutline, App.Settings.GeneralSettings.ChatTextOutline);
        SelectByTag(this.comboTextFont, App.Settings.GeneralSettings.ChatFontFamily);
        this.cbShowMessageTime.IsOn = App.Settings.GeneralSettings.ShowMessageTime;
        this.comboTheme.SelectedIndex = App.Settings.GeneralSettings.ThemeIndex == 0 ? 0 : GeneralSettings.DefaultThemeIndex;

        // Twitch Popout Chat settings
        if (App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS)
        {
            this.tbPopoutCSS.Text = CustomCSS_Defaults.TwitchPopoutChat;
            this.cbUseDefaultPopoutCSS.IsOn = true;
        }
        else
        {
            this.tbPopoutCSS.Text = App.Settings.GeneralSettings.TwitchPopoutCSS;
            this.cbUseDefaultPopoutCSS.IsOn = false;
        }

        if (Enum.IsDefined(typeof(ChatTypes), App.Settings.GeneralSettings.ChatType))
        {
            var chatType = (ChatTypes)App.Settings.GeneralSettings.ChatType;

            if (chatType == ChatTypes.CustomURL)
            {
                this.tbCSS2.Text = App.Settings.GeneralSettings.CustomCSS;
            }
            else if (chatType == ChatTypes.Padrao)
            {
                if (string.IsNullOrEmpty(App.Settings.GeneralSettings.CustomCSS))
                    this.tbCSS.Text = CustomCSS_Defaults.NoneTheme_CustomCSS;
                else
                    this.tbCSS.Text = App.Settings.GeneralSettings.CustomCSS;
            }

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
                if (!string.IsNullOrWhiteSpace(this.tbCSS2.Text) && (this.tbCSS2.Text.ToLower() != "css"))
                    App.Settings.GeneralSettings.CustomCSS = this.tbCSS2.Text;
                else
                    App.Settings.GeneralSettings.CustomCSS = string.Empty;
            }
            else if (chatType == ChatTypes.TwitchPopout)
            {
                SaveMessageTextOptions();

                if (this.cbUseDefaultPopoutCSS.IsOn)
                {
                    App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS = true;
                }
                else
                {
                    App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS = false;
                    App.Settings.GeneralSettings.TwitchPopoutCSS = this.tbPopoutCSS.Text;
                }
            }
            else if (chatType == ChatTypes.Padrao)
            {
                SaveMessageTextOptions();
                App.Settings.GeneralSettings.ThemeIndex = this.comboTheme.SelectedIndex;

                if (App.Settings.GeneralSettings.ThemeIndex == 0)
                {
                    App.Settings.GeneralSettings.CustomCSS = this.tbCSS.Text;
                }
            }
        }
    }

    // "Texto das mensagens": used by the "Padrão" and the "Chat oficial da Twitch"
    private void SaveMessageTextOptions()
    {
        App.Settings.GeneralSettings.ChatMessageColor = SelectedTag(this.comboTextColor, string.Empty);
        App.Settings.GeneralSettings.ChatTextOutline = SelectedTag(this.comboTextOutline, "none");
        App.Settings.GeneralSettings.ChatFontFamily = SelectedTag(this.comboTextFont, "theme");
        App.Settings.GeneralSettings.ShowMessageTime = this.cbShowMessageTime.IsOn;
    }

    public void ChatTypeChanged(ChatTypes chatType)
    {
        if (chatType == ChatTypes.TwitchPopout)
        {
            if (string.IsNullOrEmpty(App.Settings.GeneralSettings.TwitchPopoutCSS))
                this.tbPopoutCSS.Text = CustomCSS_Defaults.TwitchPopoutChat;
            else
                this.tbPopoutCSS.Text = App.Settings.GeneralSettings.TwitchPopoutCSS;
        }

        ShowPanelFor(chatType);
    }

    // Only the options of the chosen chat type are shown
    private void ShowPanelFor(ChatTypes chatType)
    {
        this.padraoAppearance.Visibility = chatType == ChatTypes.Padrao ? Visibility.Visible : Visibility.Collapsed;
        this.messageTextAppearance.Visibility = chatType is ChatTypes.Padrao or ChatTypes.TwitchPopout ? Visibility.Visible : Visibility.Collapsed;
        this.twitchPopoutAppearance.Visibility = chatType == ChatTypes.TwitchPopout ? Visibility.Visible : Visibility.Collapsed;
        this.customURLAppearance.Visibility = chatType == ChatTypes.CustomURL ? Visibility.Visible : Visibility.Collapsed;
    }

    // Options whose value is kept in the ComboBoxItem Tag
    private static void SelectByTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag)
            ?? combo.Items.OfType<ComboBoxItem>().First();
    }

    private static string SelectedTag(ComboBox combo, string fallback)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;
    }

    private void comboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // "Nenhum" (index 0) is the only theme that uses the custom CSS box
        lblCSS.Visibility = comboTheme.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void cbUseDefaultPopoutCSS_Toggled(object sender, RoutedEventArgs e)
    {
        if (cbUseDefaultPopoutCSS.IsOn)
        {
            tbPopoutCSS.Text = CustomCSS_Defaults.TwitchPopoutChat;
            tbPopoutCSS.IsReadOnly = true;
        }
        else
        {
            tbPopoutCSS.Text = App.Settings.GeneralSettings.TwitchPopoutCSS;
            tbPopoutCSS.IsReadOnly = false;
        }
    }
}
