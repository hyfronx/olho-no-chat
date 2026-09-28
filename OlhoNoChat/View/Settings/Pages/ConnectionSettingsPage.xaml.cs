using OlhoNoChat.Atalhos;
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
    private readonly AutorizacaoNoNavegador _autorizacao;
    private readonly ContaDaTwitch _account;
    private readonly ResgatesDePontos _resgates;

    public ConnectionSettingsPage(AutorizacaoNoNavegador autorizacao, ContaDaTwitch account, ResgatesDePontos resgates)
    {
        InitializeComponent();
        _autorizacao = autorizacao;
        _account = account;
        _resgates = resgates;

        this.Loaded += (s, e) =>
        {
            _account.Mudou += OnAccountChanged;
            _resgates.ProblemaMudou += OnRedemptionsProblemChanged;
            ShowAccount();
            ShowRedemptionsProblem();
        };
        this.Unloaded += (s, e) =>
        {
            _account.Mudou -= OnAccountChanged;
            _resgates.ProblemaMudou -= OnRedemptionsProblemChanged;
        };
    }

    public void SetupValues()
    {
        this.comboChatBox.SelectedIndex = App.Opcoes.CaixaDaTwitch ? 1 : 0;
        this.cbCloseAfterSend.IsOn = App.Opcoes.FecharCaixaDepoisDeEnviar;
        this.cbRedemptions.IsOn = App.Opcoes.MostrarResgates;
        UpdateChatBoxHint();
        ShowAccount();

        // Picture and a fresh check of the access
        if (_account.EstaConectada)
            _ = _account.VerificarAsync();
    }

    public void SaveValues()
    {
        App.Opcoes.CaixaDaTwitch = this.comboChatBox.SelectedIndex == 1;
        App.Opcoes.FecharCaixaDepoisDeEnviar = this.cbCloseAfterSend.IsOn;
        App.Opcoes.MostrarResgates = this.cbRedemptions.IsOn;
    }

    private void UpdateChatBoxHint()
    {
        Atalho hotkey = App.Opcoes.AtalhoEscrever;
        bool hasHotkey = Atalho.Existe(hotkey);
        string inGame = hasHotkey
            ? $" No jogo, aperte {hotkey} para abrir ou fechar a caixa por cima do jogo."
            : string.Empty;

        lblCloseAfterSendHint.Text = hasHotkey
            ? $"Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. Desligado, ela continua aberta para a próxima mensagem; feche com {hotkey} de novo, Esc ou o \u00D7 da caixa."
            : "Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. Desligado, ela continua aberta para a próxima mensagem.";

        lblChatBoxHint.Text = comboChatBox.SelectedIndex == 1
            ? "A caixa da Twitch, com a lista de emotes, respostas e comandos (só no Chat oficial da Twitch; no Padrão fica a do Olho no Chat). Abra com o balão de conversa, na barra laranja. Para enviar, entre na sua conta da Twitch dentro da janela do chat (uma vez): abra a caixa, clique em \"Chat\", embaixo, e depois em \"Faça login\"." + inGame
            : "A caixa \"Escrever no chat…\" embaixo do chat: abra com o balão de conversa, na barra laranja. Envia com a conta conectada acima." + inGame;
    }

    private void comboChatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateChatBoxHint();
    }

    private void OnAccountChanged()
    {
        Dispatcher.BeginInvoke(new Action(ShowAccount));
    }

    // Quando a Twitch recusa os resgates (por exemplo, canal sem pontos de canal), o motivo aparece embaixo da opção
    private void OnRedemptionsProblemChanged()
    {
        Dispatcher.BeginInvoke(new Action(ShowRedemptionsProblem));
    }

    private void ShowRedemptionsProblem()
    {
        lblRedemptionsProblem.Text = _resgates.Problema;
        lblRedemptionsProblem.Visibility = _resgates.Problema.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowAccount()
    {
        bool connecting = _autorizacao.EstaEsperando;
        bool connected = _account.EstaConectada;

        // Connected before 1.0.18: the emote list needs a new permission, given by connecting again
        bool needsNewPermission = connected && _account.PermissoesConferidas && !_account.PodeLerEmotes;

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
            lblAccount.Text = "Conectado como " + _account.NomeMostrado;
            lblAccountHint.Text = needsNewPermission
                ? "Para ver os seus emotes na caixa de escrever, clique em \"Conectar de novo\" (a Twitch pede uma permissão nova)."
                : "Você pode escrever no chat, usar os seus emotes e mostrar os resgates de pontos.";
        }
        else
        {
            lblAccount.Text = "Não conectado";
            lblAccountHint.Text = "Para só ler o chat, não precisa conectar. Conecte para escrever no chat e mostrar os resgates de pontos.";
        }

        string picture = connected ? _account.Foto : string.Empty;
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
        var waiting = _autorizacao.ConectarAsync();
        ShowAccount();
        AutorizacaoNoNavegador.Resultado resultado = await waiting;

        if (resultado.Fim == AutorizacaoNoNavegador.Fim.JaEmAndamento)
            return;
        if (resultado.Fim == AutorizacaoNoNavegador.Fim.PortasOcupadas)
        {
            ShowAccount();
            MessageBox.Show(Window.GetWindow(this), AutorizacaoNoNavegador.TextoPortasOcupadas,
                "Conectar com a Twitch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (resultado.Fim == AutorizacaoNoNavegador.Fim.Token)
        {
            lblAccount.Text = "Conectando…";
            if (!await _account.ConectarAsync(resultado.Token))
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
        _autorizacao.Cancelar();
    }

    private void btDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _account.Desconectar();
        ShowAccount();
    }
}
