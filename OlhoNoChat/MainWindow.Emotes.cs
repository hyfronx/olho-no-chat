namespace OlhoNoChat;

using OlhoNoChat.Sistema;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Twitch;

/// <summary>
/// Emote button inside the "Escrever no chat…" box: a list with the pictures of the emotes the connected
/// account can use in this channel (the channel's, the ones from its subscriptions and the global ones).
/// A click puts the emote's name in the box and keeps the list open. The list also opens the Windows
/// emoji panel. It works the same when the box was opened with the hotkey over the game.
/// Accesses given before 1.0.18 don't allow reading the account's emotes: the list then shows the
/// global ones and offers to connect again (sending keeps working).
/// </summary>
public partial class MainWindow
{
    public sealed class EmoteItem
    {
        private readonly string _url;
        private readonly int _decodeWidth;
        private BitmapImage _image;

        // decodeWidth: the picture is kept at the size shown (0: as downloaded)
        public EmoteItem(string id, string name, string scale, int decodeWidth)
        {
            Name = name;
            _url = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/static/dark/{scale}";
            _decodeWidth = decodeWidth;
        }

        public string Name { get; }

        // Downloaded the first time the emote is shown, then kept
        public ImageSource Image => _image ??= CreateImage();

        private BitmapImage CreateImage()
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(_url);
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (_decodeWidth > 0)
                image.DecodePixelWidth = _decodeWidth;
            image.EndInit();
            return image;
        }
    }

    public sealed record EmoteGroupItem(string Title, IReadOnlyList<EmoteItem> Emotes);

    private List<EmoteGroupItem> _emoteGroups;
    private ListaDeEmotes.Lista _emotesList; // the list _emoteGroups was built from
    private bool _emotesOnlyGlobal;
    private DateTime _emotePopupClosedAt = DateTime.MinValue;
    private DispatcherTimer _emoteSearchTimer;

    // The emote button sits where the box shows its "x" (clear) button while typing: that one is made
    // invisible (the box shows it with an animation, which wins over Visibility)
    private void HideMessageBoxClearButton()
    {
        tbChatMessage.ApplyTemplate();
        if (tbChatMessage.Template?.FindName("DeleteButton", tbChatMessage) is UIElement clear)
        {
            clear.Opacity = 0;
            clear.IsHitTestVisible = false;
        }
    }

    private void btnEmotes_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // With the list open, the button closes it
        if (EmotePopup.IsOpen)
        {
            e.Handled = true;
            CloseEmotePopup();
        }
    }

    private void btnEmotes_Click(object sender, RoutedEventArgs e)
    {
        // The click that closed the list (outside it) must not open it again
        if (DateTime.UtcNow - _emotePopupClosedAt < TimeSpan.FromMilliseconds(300))
            return;

        _ = OpenEmotePopupAsync();
    }

    private async Task OpenEmotePopupAsync()
    {
        tbEmoteSearch.Text = string.Empty;
        _emoteSearchTimer?.Stop(); // the full list is shown below
        EmotePopup.IsOpen = true;
        FocusMessageBox();
        await LoadEmotesAsync();
    }

    private void CloseEmotePopup()
    {
        EmotePopup.IsOpen = false;
        FocusMessageBox();
    }

    private void EmotePopup_Closed(object sender, EventArgs e)
    {
        _emotePopupClosedAt = DateTime.UtcNow;
        _emoteSearchTimer?.Stop();
    }

    private void FocusMessageBox()
    {
        if (MessageBar.Visibility != Visibility.Visible)
            return;
        tbChatMessage.Focus();
        Keyboard.Focus(tbChatMessage);
    }

    // Twitch sends the list in many small pages (a few seconds): it starts loading as soon as the
    // message box gets the focus, so the list is usually ready when the button is clicked
    private void PreloadEmotes()
    {
        if (ChatInputAvailable && !_listaDeEmotes.EstaPronta(ChatChannel))
            _ = LoadEmotesAsync();
    }

    // A busca que já estiver em andamento (a do foco na caixa) é reaproveitada pela ListaDeEmotes
    private async Task LoadEmotesAsync()
    {
        string channel = ChatChannel;
        if (!_listaDeEmotes.EstaPronta(channel) || _emoteGroups == null)
        {
            EmoteGroups.ItemsSource = null;
            EmoteNotice.Visibility = Visibility.Collapsed;
            ShowEmoteStatus("Carregando emotes…");
        }
        try
        {
            await _conta.VerificarUmaVezAsync();
            ListaDeEmotes.Lista list = await _listaDeEmotes.BuscarAsync(channel);
            if (_emoteGroups == null || !ReferenceEquals(list, _emotesList))
            {
                // Sharp pictures on screens with a bigger text size: the double-size file, kept at the size shown
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                string scale = dpi > 1.2 ? "2.0" : "1.0";
                int decodeWidth = dpi > 1.2 && dpi < 2.0 ? (int)Math.Ceiling(28 * dpi) : 0;
                _emoteGroups = list.Grupos
                    .Select(g => new EmoteGroupItem(g.Titulo, g.Emotes.Select(emote => new EmoteItem(emote.Id, emote.Nome, scale, decodeWidth)).ToList()))
                    .ToList();
                _emotesList = list;
                _emotesOnlyGlobal = list.SoGlobais;
            }
            ShowEmotes();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the emotes.");
            ShowEmoteStatus("Não foi possível carregar os emotes. Confira sua internet e tente de novo.");
        }
    }

    private void ShowEmotes()
    {
        if (_emoteGroups == null)
            return;

        string search = tbEmoteSearch.Text.Trim();
        List<EmoteGroupItem> groups = search.Length == 0
            ? _emoteGroups
            : _emoteGroups.Select(g => new EmoteGroupItem(g.Title, g.Emotes.Where(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList()))
                          .Where(g => g.Emotes.Count > 0)
                          .ToList();

        EmoteGroups.ItemsSource = groups;
        EmoteScroll.ScrollToTop();
        EmoteNotice.Visibility = _emotesOnlyGlobal ? Visibility.Visible : Visibility.Collapsed;
        ShowEmoteStatus(groups.Count > 0 ? null
            : search.Length > 0 ? $"Nenhum emote com \"{search}\"."
            : "Nenhum emote para mostrar.");
    }

    private void ShowEmoteStatus(string text)
    {
        tbEmoteStatus.Text = text ?? string.Empty;
        tbEmoteStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Filtered once typing pauses: a big list takes a moment to build again
    private void tbEmoteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_emoteSearchTimer == null)
        {
            _emoteSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _emoteSearchTimer.Tick += (s, args) =>
            {
                _emoteSearchTimer.Stop();
                ShowEmotes();
            };
        }
        _emoteSearchTimer.Stop();
        _emoteSearchTimer.Start();
    }

    private void tbEmoteSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseEmotePopup();
        }
        else if (e.Key == Key.Enter)
        {
            // The first emote found goes to the message (of the text typed, also before the pause)
            e.Handled = true;
            if (_emoteSearchTimer?.IsEnabled == true)
            {
                _emoteSearchTimer.Stop();
                ShowEmotes();
            }
            var first = (EmoteGroups.ItemsSource as IEnumerable<EmoteGroupItem>)?.SelectMany(g => g.Emotes).FirstOrDefault();
            if (first != null)
                InsertEmote(first.Name);
        }
    }

    private void Emote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmoteItem emote)
            InsertEmote(emote.Name);
    }

    // Puts the emote where the cursor is in the message, with spaces around it (Twitch needs them)
    private void InsertEmote(string name)
    {
        TextBox box = tbChatMessage;
        int start = box.SelectionStart;
        string before = box.Text.Substring(0, start);
        string after = box.Text.Substring(start + box.SelectionLength);
        string text = (before.Length > 0 && !char.IsWhiteSpace(before[^1]) ? " " : string.Empty)
                      + name
                      + (after.Length > 0 && char.IsWhiteSpace(after[0]) ? string.Empty : " ");

        if (box.Text.Length - box.SelectionLength + text.Length > box.MaxLength)
        {
            ShowChatInputStatus("A mensagem já está no tamanho máximo da Twitch (500 letras).");
            return;
        }

        box.SelectedText = text;
        box.SelectionStart = start + text.Length;
        box.SelectionLength = 0;
        FocusMessageBox();
    }

    // Windows emoji panel (the same as Win + .): it writes in the focused box
    private void btnEmojiPanel_Click(object sender, RoutedEventArgs e)
    {
        CloseEmotePopup();
        Dispatcher.BeginInvoke(new Action(JanelaDoWindows.AbrirPainelDeEmojis), DispatcherPriority.Input);
    }

    // "Conectar de novo" in the list: the same as in the Twitch tab (the browser asks for the new permission)
    private async void btnEmoteReconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_autorizacao.EstaEsperando)
            return;

        CloseEmotePopup();
        ShowChatInputStatus("Termine no navegador que abriu: clique em \"Autorizar\" na página da Twitch.");

        AutorizacaoNoNavegador.Resultado resultado = await _autorizacao.ConectarAsync();
        switch (resultado.Fim)
        {
            case AutorizacaoNoNavegador.Fim.PortasOcupadas:
                ShowChatInputStatus(AutorizacaoNoNavegador.TextoPortasOcupadas);
                return;
            case AutorizacaoNoNavegador.Fim.Token:
                bool connected = await _conta.ConectarAsync(resultado.Token);
                _emoteGroups = null; // load again with the new permission
                _listaDeEmotes.Descartar();
                ShowChatInputStatus(connected ? null : "A Twitch não confirmou o acesso. Tente de novo na aba Twitch das Configurações.");
                return;
            default:
                ShowChatInputStatus(null);
                return;
        }
    }
}
