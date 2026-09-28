namespace OlhoNoChat;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Chat;
using OlhoNoChat.Twitch;

/// <summary>
/// Channel strip under the title bar, to change the channel without opening Configurações.
/// Shown only with the borders visible and for the chat types that show a Twitch channel ("Padrão" and
/// "Chat oficial da Twitch"). With no channel it stays open with the name box; with a channel it shows
/// just the name, and a click opens it. It is the only place to change the channel; saved right away.
/// Changing the channel loads the chat page again (only when the channel really changed);
/// "Sair do canal" goes back to the welcome page.
/// </summary>
public partial class MainWindow
{
    private bool _channelEditorOpen = false;
    private bool _checkingChannel = false;

    // "Padrão" and "Chat oficial da Twitch" show a channel's chat (and can write in it)
    private static bool IsChannelChatType(int chatType) => TiposDeChat.UsaCanal(TiposDeChat.Ler(chatType));

    private static bool ChatTypeUsesChannel => IsChannelChatType(App.Opcoes.TipoDeChat);

    // The saved channel as a name (older versions could save a link)
    private static string SavedChannel => PaginaDoChat.CanalSalvo(App.Opcoes);

    private void UpdateChannelBar()
    {
        bool show = ChatTypeUsesChannel && !_hiddenBorders;
        string channel = SavedChannel;
        bool hasChannel = channel.Length > 0;

        if (!show || !hasChannel)
            _channelEditorOpen = false;
        bool editorOpen = !hasChannel || _channelEditorOpen;

        ChannelBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ChannelEditor.Visibility = editorOpen ? Visibility.Visible : Visibility.Collapsed;
        btnChannelClosed.Visibility = editorOpen ? Visibility.Collapsed : Visibility.Visible;

        tbChannelName.Text = channel;
        UpdateChannelStatusDot();

        // Changing channel: cancel and leave; first channel: only the button
        btnChannelApply.Content = hasChannel ? "Trocar" : "Entrar no chat";
        btnChannelCancel.Visibility = hasChannel ? Visibility.Visible : Visibility.Collapsed;
        btnLeaveChannel.Visibility = hasChannel ? Visibility.Visible : Visibility.Collapsed;
        ShowChannelHint(null);
    }

    private void ShowChannelHint(string problem)
    {
        tbChannelHint.Text = problem ?? "Ex.: seucanal, @seucanal ou o link do canal";
        tbChannelHint.Foreground = (System.Windows.Media.Brush)FindResource(problem == null ? "BarHintBrush" : "BarErrorBrush");
    }

    private void btnChannelClosed_Click(object sender, RoutedEventArgs e)
    {
        OpenChannelEditor();
    }

    private void OpenChannelEditor()
    {
        _channelEditorOpen = true;
        tbChannel.Text = SavedChannel;
        UpdateChannelBar();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            tbChannel.Focus();
            Keyboard.Focus(tbChannel);
            tbChannel.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseChannelEditor()
    {
        _channelEditorOpen = false;
        tbChannel.Text = SavedChannel;
        UpdateChannelBar();
    }

    private void btnChannelCancel_Click(object sender, RoutedEventArgs e)
    {
        CloseChannelEditor();
    }

    private void tbChannel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = ApplyChannelAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (SavedChannel.Length > 0)
                CloseChannelEditor();
            else
                ShowChannelHint(null);
        }
    }

    private void tbChannel_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_checkingChannel)
            ShowChannelHint(null);
    }

    private void btnChannelApply_Click(object sender, RoutedEventArgs e)
    {
        _ = ApplyChannelAsync();
    }

    private async Task ApplyChannelAsync()
    {
        if (_checkingChannel)
            return;

        string name = NomesDaTwitch.Extrair(tbChannel.Text);
        if (name.Length == 0)
        {
            ShowChannelHint("Digite o nome do canal.");
            tbChannel.Focus();
            return;
        }
        if (!NomesDaTwitch.EhValido(name))
        {
            ShowChannelHint(NomesDaTwitch.DicaNomeInvalido);
            tbChannel.Focus();
            return;
        }

        name = name.ToLowerInvariant();

        // Same channel: nothing to load again (that would clear the messages on screen)
        if (string.Equals(name, SavedChannel, StringComparison.OrdinalIgnoreCase))
        {
            CloseChannelEditor();
            return;
        }

        // With a connected account, Twitch can tell if the channel exists (otherwise the chat just stays empty)
        if (_conta.EstaConectada)
        {
            _checkingChannel = true;
            btnChannelApply.IsEnabled = false;
            ShowChannelHint("Procurando o canal…");
            bool? exists;
            try
            {
                exists = await _conta.CanalExisteAsync(name);
            }
            finally
            {
                _checkingChannel = false;
                btnChannelApply.IsEnabled = true;
            }

            if (exists == false)
            {
                ShowChannelHint($"Não achei o canal \"{name}\" na Twitch. Confira o nome.");
                tbChannel.Focus();
                return;
            }
        }

        SetChannel(name);
    }

    private void btnLeaveChannel_Click(object sender, RoutedEventArgs e)
    {
        SetChannel(string.Empty);
    }

    // Saves the channel and loads its chat, or the welcome page
    private void SetChannel(string channel)
    {
        _logger.LogInformation("Channel changed on the channel strip.");
        App.Opcoes.Canal = channel;
        App.ArquivoDeConfiguracoes.Gravar();

        _channelEditorOpen = false;
        tbChannel.Text = channel;
        UpdateChannelBar();

        // The chat of the channel, or the welcome page without one
        _navegador.AbrirDasOpcoes();
        UpdateChatInput();
    }

    // --- Connection dot ------------------------------------------------------------------------------
    // "Padrão": the page reports its connection to Twitch (onc:chat-state:..., see browser/chat.js).
    // "Chat oficial da Twitch": the page itself connects, so the dot follows the page loading.

    private enum ChatConnection { None, Connecting, Connected, Disconnected }
    private ChatConnection _chatConnection = ChatConnection.None;
    private bool _statusDotPulsing = false;

    private static readonly SolidColorBrush ConnectingBrush = CreateFrozenBrush(0xF5, 0xC5, 0x18);
    private static readonly SolidColorBrush ConnectedBrush = CreateFrozenBrush(0x3B, 0xD1, 0x6F);
    private static readonly SolidColorBrush DisconnectedBrush = CreateFrozenBrush(0xF0, 0x4A, 0x4A);

    private void SetChatConnection(ChatConnection state)
    {
        _chatConnection = state;
        UpdateChannelStatusDot();
    }

    // A chat page starts loading: only the channel chats have a dot
    private void OnChatPageLoading()
    {
        SetChatConnection(_navegador.Pagina?.Canal != null ? ChatConnection.Connecting : ChatConnection.None);
    }

    private void OnChatPageLoaded(bool success)
    {
        if (!success)
            SetChatConnection(_chatConnection == ChatConnection.None ? ChatConnection.None : ChatConnection.Disconnected);
        else if (_navegador.Pagina is ChatOficialDaTwitch)
            SetChatConnection(ChatConnection.Connected);
    }

    // The "Padrão" page reports its connection
    private void OnChatStateMessage(ChatConnection state)
    {
        if (_navegador.Pagina is ChatPadrao)
            SetChatConnection(state);
    }

    private void UpdateChannelStatusDot()
    {
        string channel = SavedChannel;
        (Brush brush, string text) = _chatConnection switch
        {
            ChatConnection.Connecting => ((Brush)ConnectingBrush, $"Conectando ao chat de {channel}…"),
            ChatConnection.Connected => (ConnectedBrush, $"Conectado ao chat de {channel}."),
            ChatConnection.Disconnected => (DisconnectedBrush, "Sem conexão com o chat. Tentando de novo…"),
            _ => (null, null)
        };

        ChannelStatusDot.Visibility = brush != null ? Visibility.Visible : Visibility.Collapsed;
        ChannelStatusDot.Fill = brush;
        btnChannelClosed.ToolTip = (text != null ? text + "\n" : string.Empty) + "Clique para trocar de canal.";

        // Pulsing while connecting, only while the dot is on screen: a running animation redraws the window
        // every frame (also with the strip hidden over the game). 15 frames a second are enough for the slow fade.
        bool pulse = _chatConnection == ChatConnection.Connecting
                     && ChannelBar.Visibility == Visibility.Visible && btnChannelClosed.Visibility == Visibility.Visible;
        if (pulse == _statusDotPulsing)
            return;

        _statusDotPulsing = pulse;
        if (pulse)
        {
            var animation = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(animation, 15);
            ChannelStatusDot.BeginAnimation(UIElement.OpacityProperty, animation);
        }
        else
        {
            ChannelStatusDot.BeginAnimation(UIElement.OpacityProperty, null);
        }
    }
}
