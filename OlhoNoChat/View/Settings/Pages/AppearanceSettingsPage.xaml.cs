using System.Windows;
using System.Windows.Controls;
using OlhoNoChat.Configuracoes;

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
        SelectByTag(this.comboTextColor, App.Opcoes.CorDoTexto ?? string.Empty);
        SelectByTag(this.comboTextOutline, App.Opcoes.ContornoDasLetras);
        SelectByTag(this.comboTextFont, App.Opcoes.Fonte);
        this.cbShowMessageTime.IsOn = App.Opcoes.MostrarHorario;
        this.comboTheme.SelectedIndex = App.Opcoes.Tema == Opcoes.TemaNenhum ? Opcoes.TemaNenhum : Opcoes.TemaPadrao;

        // Twitch Popout Chat settings
        if (App.Opcoes.AparenciaPadraoNoChatOficial)
        {
            this.tbPopoutCSS.Text = CustomCSS_Defaults.TwitchPopoutChat;
            this.cbUseDefaultPopoutCSS.IsOn = true;
        }
        else
        {
            this.tbPopoutCSS.Text = App.Opcoes.CssDoChatOficial;
            this.cbUseDefaultPopoutCSS.IsOn = false;
        }

        // Each has its own CSS, so switching the chat type before saving shows the one of the new type. With nothing
        // saved for a type that isn't the saved one, its editor keeps the example text of the XAML.
        if (App.Opcoes.TipoDeChat == (int)ChatTypes.CustomURL || App.Opcoes.CssDoEnderecoPersonalizado.Length > 0)
            this.tbCSS2.Text = App.Opcoes.CssDoEnderecoPersonalizado;
        if (App.Opcoes.TipoDeChat == (int)ChatTypes.Padrao || App.Opcoes.CssDoTemaNenhum.Length > 0)
            this.tbCSS.Text = string.IsNullOrEmpty(App.Opcoes.CssDoTemaNenhum)
                ? CustomCSS_Defaults.NoneTheme_CustomCSS
                : App.Opcoes.CssDoTemaNenhum;

        if (Enum.IsDefined(typeof(ChatTypes), App.Opcoes.TipoDeChat))
            ShowPanelFor((ChatTypes)App.Opcoes.TipoDeChat);
    }

    public void SaveValues()
    {
        if (Enum.IsDefined(typeof(ChatTypes), App.Opcoes.TipoDeChat))
        {
            var chatType = (ChatTypes)App.Opcoes.TipoDeChat;

            if (chatType == ChatTypes.CustomURL)
            {
                if (!string.IsNullOrWhiteSpace(this.tbCSS2.Text) && (this.tbCSS2.Text.ToLower() != "css"))
                    App.Opcoes.CssDoEnderecoPersonalizado = this.tbCSS2.Text;
                else
                    App.Opcoes.CssDoEnderecoPersonalizado = string.Empty;
            }
            else if (chatType == ChatTypes.TwitchPopout)
            {
                SaveMessageTextOptions();

                if (this.cbUseDefaultPopoutCSS.IsOn)
                {
                    App.Opcoes.AparenciaPadraoNoChatOficial = true;
                }
                else
                {
                    App.Opcoes.AparenciaPadraoNoChatOficial = false;
                    App.Opcoes.CssDoChatOficial = this.tbPopoutCSS.Text;
                }
            }
            else if (chatType == ChatTypes.Padrao)
            {
                SaveMessageTextOptions();
                App.Opcoes.Tema = this.comboTheme.SelectedIndex;

                if (App.Opcoes.Tema == Opcoes.TemaNenhum)
                {
                    App.Opcoes.CssDoTemaNenhum = this.tbCSS.Text;
                }
            }
        }
    }

    // "Texto das mensagens": used by the "Padrão" and the "Chat oficial da Twitch"
    private void SaveMessageTextOptions()
    {
        App.Opcoes.CorDoTexto = SelectedTag(this.comboTextColor, string.Empty);
        App.Opcoes.ContornoDasLetras = SelectedTag(this.comboTextOutline, "none");
        App.Opcoes.Fonte = SelectedTag(this.comboTextFont, "theme");
        App.Opcoes.MostrarHorario = this.cbShowMessageTime.IsOn;
    }

    public void ChatTypeChanged(ChatTypes chatType)
    {
        if (chatType == ChatTypes.TwitchPopout)
        {
            if (string.IsNullOrEmpty(App.Opcoes.CssDoChatOficial))
                this.tbPopoutCSS.Text = CustomCSS_Defaults.TwitchPopoutChat;
            else
                this.tbPopoutCSS.Text = App.Opcoes.CssDoChatOficial;
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
            tbPopoutCSS.Text = App.Opcoes.CssDoChatOficial;
            tbPopoutCSS.IsReadOnly = false;
        }
    }
}
