using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OlhoNoChat.Twitch;
using MessageBox = System.Windows.MessageBox;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Twitch" tab: connect the Twitch account (in the user's browser), the "Escrever no chat…" box
/// and the channel point redemptions. Connecting/disconnecting happens right away; the two options
/// are saved with "Salvar" like the other tabs.
/// </summary>
public partial class ConnectionSettingsPage : UserControl
{
    private readonly ITwitchAuthService _twitchAuthService;
    private readonly TwitchAccount _account;

    public ConnectionSettingsPage(ITwitchAuthService twitchAuthService, TwitchAccount account)
    {
        InitializeComponent();
        _twitchAuthService = twitchAuthService;
        _account = account;

        this.Loaded += (s, e) =>
        {
            _account.Changed += OnAccountChanged;
            ShowAccount();
        };
        this.Unloaded += (s, e) => _account.Changed -= OnAccountChanged;
    }

    public void SetupValues()
    {
        this.comboChatBox.SelectedIndex = App.Settings.GeneralSettings.UseTwitchChatBox ? 1 : 0;
        this.cbCloseAfterSend.IsOn = App.Settings.GeneralSettings.CloseChatBoxAfterSend;
        this.cbRedemptions.IsOn = App.Settings.GeneralSettings.RedemptionsEnabled;
        UpdateChatBoxHint();
        ShowAccount();

        // Picture and a fresh check of the access
        if (_account.IsConnected)
            _ = _account.CheckAsync();
    }

    public void SaveValues()
    {
        App.Settings.GeneralSettings.UseTwitchChatBox = this.comboChatBox.SelectedIndex == 1;
        App.Settings.GeneralSettings.CloseChatBoxAfterSend = this.cbCloseAfterSend.IsOn;
        App.Settings.GeneralSettings.RedemptionsEnabled = this.cbRedemptions.IsOn;
    }

    private void UpdateChatBoxHint()
    {
        Hotkey hotkey = App.Settings.GeneralSettings.WriteMessageHotkey;
        bool hasHotkey = hotkey != null && hotkey.Key != Key.None;
        string inGame = hasHotkey
            ? $" No jogo, aperte {hotkey} para abrir ou fechar a caixa por cima do jogo."
            : string.Empty;

        lblCloseAfterSendHint.Text = hasHotkey
            ? $"Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. Desligado, ela continua aberta para a próxima mensagem; feche com {hotkey} de novo, Esc ou o \u00D7 da caixa."
            : "Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. Desligado, ela continua aberta para a próxima mensagem.";

        lblChatBoxHint.Text = comboChatBox.SelectedIndex == 1
            ? "A caixa da Twitch, com a lista de emotes, respostas e comandos (só no Chat oficial da Twitch; no Padrão fica a do Olho no Chat). Abra com Escrever, na barra laranja. Para enviar, entre na sua conta da Twitch dentro da janela do chat (uma vez): abra a caixa, clique em \"Chat\", embaixo, e depois em \"Faça login\"." + inGame
            : "A caixa \"Escrever no chat…\" embaixo do chat: abra com Escrever, na barra laranja. Envia com a conta conectada acima." + inGame;
    }

    private void comboChatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateChatBoxHint();
    }

    private void OnAccountChanged()
    {
        Dispatcher.BeginInvoke(new Action(ShowAccount));
    }

    private void ShowAccount()
    {
        bool connecting = _twitchAuthService.IsConnecting;
        bool connected = _account.IsConnected;

        // Connected before 1.0.18: the emote list needs a new permission, given by connecting again
        bool needsNewPermission = connected && _account.PermissionsChecked && !_account.CanReadEmotes;

        btConnect.Visibility = (!connected || needsNewPermission) && !connecting ? Visibility.Visible : Visibility.Collapsed;
        btConnect.Content = connected ? "Conectar de novo" : "Conectar";
        btConnect.Margin = new Thickness(0, 0, connected ? 8 : 0, 0);
        btCancelConnect.Visibility = connecting ? Visibility.Visible : Visibility.Collapsed;
        btDisconnect.Visibility = connected && !connecting ? Visibility.Visible : Visibility.Collapsed;

        if (connecting)
        {
            lblAccount.Text = "Esperando você autorizar…";
            lblAccountHint.Text = "Termine no navegador que abriu: clique em \"Autorizar\" na página da Twitch.";
        }
        else if (connected)
        {
            lblAccount.Text = "Conectado como " + _account.DisplayName;
            lblAccountHint.Text = needsNewPermission
                ? "Para ver os seus emotes na caixa de escrever, clique em \"Conectar de novo\" (a Twitch pede uma permissão nova)."
                : "Você pode escrever no chat, usar os seus emotes e mostrar os resgates de pontos.";
        }
        else
        {
            lblAccount.Text = "Não conectado";
            lblAccountHint.Text = "Para só ler o chat, não precisa conectar. Conecte para escrever no chat e mostrar os resgates de pontos.";
        }

        string picture = connected ? _account.ProfileImageUrl : string.Empty;
        if (!string.IsNullOrEmpty(picture))
        {
            try
            {
                imgAccountBrush.ImageSource = new BitmapImage(new Uri(picture));
                imgAccount.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                imgAccount.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            imgAccount.Visibility = Visibility.Collapsed;
        }
    }

    private async void btConnect_Click(object sender, RoutedEventArgs e)
    {
        string token;
        try
        {
            var waiting = _twitchAuthService.ConnectAsync();
            ShowAccount();
            token = await waiting;
        }
        catch (HttpListenerException)
        {
            ShowAccount();
            MessageBox.Show(Window.GetWindow(this),
                "Não foi possível esperar a resposta da Twitch: outros programas estão usando as portas do computador que o Olho no Chat usa. Tente de novo daqui a pouco.",
                "Conectar com a Twitch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(token))
        {
            lblAccount.Text = "Conectando…";
            if (!await _account.ConnectAsync(token))
            {
                MessageBox.Show(Window.GetWindow(this),
                    "A Twitch não confirmou o acesso. Confira sua internet e tente de novo.",
                    "Conectar com a Twitch", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        ShowAccount();
        Window.GetWindow(this)?.Activate();
    }

    private void btCancelConnect_Click(object sender, RoutedEventArgs e)
    {
        _twitchAuthService.Cancel();
    }

    private void btDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _account.Disconnect();
        ShowAccount();
    }
}
