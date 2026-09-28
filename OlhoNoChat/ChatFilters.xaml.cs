using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OlhoNoChat.Twitch;
using ContentDialog = ModernWpf.Controls.ContentDialog;
using ContentDialogButton = ModernWpf.Controls.ContentDialogButton;
using ContentDialogResult = ModernWpf.Controls.ContentDialogResult;
using FontIcon = ModernWpf.Controls.FontIcon;

namespace OlhoNoChat
{
    /// <summary>
    /// "Filtros do chat" window: the name lists of highlighted and blocked users and their colors.
    /// </summary>
    public partial class ChatFilters : Window
    {
        // Raised after "Salvar": the settings are saved and applied to the chat right away
        public event Action Saved;

        private readonly ObservableCollection<string> allowedUsers = new ObservableCollection<string>();
        private readonly ObservableCollection<string> blockedUsers = new ObservableCollection<string>();

        private readonly HighlightColorChoice highlightColor;
        private readonly HighlightColorChoice modsColor;
        private readonly HighlightColorChoice vipsColor;

        // What the window showed when it opened or was last saved, to know if something changed
        private string savedState;
        private bool closeConfirmed = false;
        private readonly DispatcherTimer savedFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };

        public ChatFilters()
        {
            InitializeComponent();

            foreach (string s in App.Opcoes.ListaDeUsuarios)
                allowedUsers.Add(s);

            foreach (string s in App.Opcoes.UsuariosBloqueados)
                blockedUsers.Add(s);

            lvAllowedUsernames.ItemsSource = allowedUsers;
            lvBlockedUsernames.ItemsSource = blockedUsers;
            allowedUsers.CollectionChanged += (s, e) => UpdateEmptyLists();
            blockedUsers.CollectionChanged += (s, e) => UpdateEmptyLists();
            UpdateEmptyLists();

            highlightColor = new HighlightColorChoice(colorPicker, tbCustomColor, tbCustomColorError, App.Opcoes.CorDoDestaque);
            modsColor = new HighlightColorChoice(colorPickerMods, tbCustomColorMods, tbCustomColorModsError, App.Opcoes.CorDosModeradores);
            vipsColor = new HighlightColorChoice(colorPickerVIPs, tbCustomColorVIPs, tbCustomColorVIPsError, App.Opcoes.CorDosVips);
            this.cbAllMods.IsOn = App.Opcoes.DestacarModeradores;
            this.cbAllVIPs.IsOn = App.Opcoes.DestacarVips;

            // The list either shows only its users or highlights them (never both)
            if (App.Opcoes.SoUsuariosDaLista)
                this.cbAllowedUsers.IsChecked = true;
            else if (App.Opcoes.DestacarUsuarios)
                this.cbHighlightUsers.IsChecked = true;
            else
                this.rbListOff.IsChecked = true;

            lvFilters.SelectedIndex = 0;

            savedState = CurrentState();
            savedFeedbackTimer.Tick += (s, e) => ShowSaveButtonNormal();
        }

        private string CurrentState()
        {
            return string.Join("|",
                cbHighlightUsers.IsChecked == true, cbAllowedUsers.IsChecked == true, cbAllMods.IsOn, cbAllVIPs.IsOn,
                string.Join(",", allowedUsers), string.Join(",", blockedUsers),
                highlightColor.Color, modsColor.Color, vipsColor.Color);
        }

        // Highlight backgrounds are see-through, like the default ones
        private const byte HighlightAlpha = 0x96;

        private static (string Name, Color Color)[] HighlightColors()
        {
            (string, Color) Item(string name, byte r, byte g, byte b) => (name, Color.FromArgb(HighlightAlpha, r, g, b));

            return new[]
            {
                Item("Amarelo", 0xF5, 0xF5, 0x00),
                Item("Laranja", 0xFF, 0x8A, 0x00),
                Item("Vermelho", 0xE5, 0x39, 0x35),
                Item("Rosa", 0xDB, 0x33, 0xB3),
                Item("Roxo", 0x8A, 0x2B, 0xE2),
                Item("Azul", 0x1E, 0x90, 0xFF),
                Item("Azul-claro", 0x00, 0xBC, 0xD4),
                Item("Verde", 0x00, 0xAD, 0x03),
                Item("Branco", 0xFF, 0xFF, 0xFF),
                Item("Cinza", 0x80, 0x80, 0x80),
            };
        }

        private void UpdateEmptyLists()
        {
            tbAllowedEmpty.Visibility = allowedUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tbBlockedEmpty.Visibility = blockedUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Accepts "nome", "@nome", "twitch.tv/nome" or the channel link; returns null (and shows why) when it isn't a user name
        private static string ReadUserName(TextBox input, TextBlock error, ObservableCollection<string> list)
        {
            string name = NomesDaTwitch.Extrair(input.Text);

            string problem = null;
            if (name.Length == 0)
                problem = "Digite o nome do usuário.";
            else if (!NomesDaTwitch.EhValido(name))
                problem = NomesDaTwitch.DicaNomeInvalido;
            else if (list.Any(u => string.Equals(u, name, StringComparison.OrdinalIgnoreCase)))
                problem = name + " já está na lista.";

            error.Text = problem ?? string.Empty;
            error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
            return problem == null ? name : null;
        }

        private void AddUser(TextBox input, TextBlock error, ObservableCollection<string> list)
        {
            string name = ReadUserName(input, error, list);
            if (name != null)
            {
                list.Add(name);
                input.Clear();
            }
            input.Focus();
        }

        private void OnClick_AddAllowedUsername(object sender, RoutedEventArgs e)
        {
            AddUser(tbNewAllowedUser, tbAllowedUserError, allowedUsers);
        }

        private void OnClick_AddBlockedUsername(object sender, RoutedEventArgs e)
        {
            AddUser(tbNewBlockedUser, tbBlockedUserError, blockedUsers);
        }

        // Enter adds the name (instead of saving the window)
        private void NewAllowedUser_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddUser(tbNewAllowedUser, tbAllowedUserError, allowedUsers);
                e.Handled = true;
            }
        }

        private void NewBlockedUser_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddUser(tbNewBlockedUser, tbBlockedUserError, blockedUsers);
                e.Handled = true;
            }
        }

        private void RemoveUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: string name })
            {
                // The same template is used by both lists
                if (!allowedUsers.Remove(name))
                    blockedUsers.Remove(name);
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        private void Save()
        {
            // A name typed but not added yet goes in too
            if (tbNewAllowedUser.Text.Trim().Length > 0)
                AddUser(tbNewAllowedUser, tbAllowedUserError, allowedUsers);
            if (tbNewBlockedUser.Text.Trim().Length > 0)
                AddUser(tbNewBlockedUser, tbBlockedUserError, blockedUsers);

            App.Opcoes.DestacarUsuarios = this.cbHighlightUsers.IsChecked ?? false;
            App.Opcoes.SoUsuariosDaLista = this.cbAllowedUsers.IsChecked ?? false;
            App.Opcoes.DestacarModeradores = this.cbAllMods.IsOn;
            App.Opcoes.DestacarVips = this.cbAllVIPs.IsOn;
            App.Opcoes.ListaDeUsuarios = allowedUsers.ToList();
            App.Opcoes.UsuariosBloqueados = blockedUsers.ToList();
            App.Opcoes.CorDoDestaque = highlightColor.Save();
            App.Opcoes.CorDosModeradores = modsColor.Save();
            App.Opcoes.CorDosVips = vipsColor.Save();
            App.ArquivoDeConfiguracoes.Gravar();

            savedState = CurrentState();
            Saved?.Invoke();
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

            savedFeedbackTimer.Stop();
            savedFeedbackTimer.Start();
        }

        private void ShowSaveButtonNormal()
        {
            savedFeedbackTimer.Stop();
            OKButton.Content = "Salvar";
            System.Windows.Automation.AutomationProperties.SetName(OKButton, "Salvar");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Sistema.MolduraDaJanela.Aplicar(this); // own close button, no gray line around
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Esc closes the window, unless it is for an open list (see SettingsWindow)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !SettingsWindow.EscapeBelongsToFocusedControl())
            {
                e.Handled = true;
                Close();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (closeConfirmed || CurrentState() == savedState)
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
                Content = "Você mudou os filtros e ainda não salvou.",
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

            closeConfirmed = true;
            Close();
        }

        // Moderator/VIP options and colours depend on what the list is used for
        private void ListMode_Checked(object sender, RoutedEventArgs e)
        {
            // Called while the window is still being built, before every element exists
            if (modsRow == null || vipsRow == null)
                return;

            bool highlight = cbHighlightUsers.IsChecked == true;
            bool onlyList = cbAllowedUsers.IsChecked == true;

            modsRow.IsEnabled = highlight || onlyList;
            vipsRow.IsEnabled = highlight || onlyList;
            tbModsVipsOff.Visibility = highlight || onlyList ? Visibility.Collapsed : Visibility.Visible;

            lblAllMods.Text = onlyList ? "Mostrar também todos os moderadores" : "Destacar todos os moderadores";
            lblAllVIPs.Text = onlyList ? "Mostrar também todos os VIPs" : "Destacar todos os VIPs";

            // Colours are only used when highlighting
            highlightColorRow.Visibility = highlight ? Visibility.Visible : Visibility.Collapsed;
            colorPickerMods.Visibility = onlyList ? Visibility.Collapsed : Visibility.Visible;
            colorPickerVIPs.Visibility = onlyList ? Visibility.Collapsed : Visibility.Visible;
        }

        private void lvFilters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (this.lvFilters.SelectedIndex)
            {
                case 0: // Allowed usernames/ Highlighting
                    filterUsernamesGrid.Visibility = Visibility.Visible;
                    filterBotsGrid.Visibility = Visibility.Collapsed;
                    break;
                case 1: // Blocked usernames/ Bots
                    filterUsernamesGrid.Visibility = Visibility.Collapsed;
                    filterBotsGrid.Visibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// One highlight colour: a list of the ready colours plus "Personalizada…", which shows a box to type a #RRGGBB colour.
        /// </summary>
        private sealed class HighlightColorChoice
        {
            private readonly ComboBox list;
            private readonly TextBox customBox;
            private readonly TextBlock customError;
            private readonly ComboBoxItem customItem;
            private readonly Border customSwatch = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)) };
            private Color saved;

            public HighlightColorChoice(ComboBox list, TextBox customBox, TextBlock customError, Color saved)
            {
                this.list = list;
                this.customBox = customBox;
                this.customError = customError;
                this.saved = saved;

                var swatchStyle = (Style)list.FindResource("ColorSwatch");
                ComboBoxItem Item(string name, Border swatch)
                {
                    swatch.Style = swatchStyle;
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    content.Children.Add(swatch);
                    content.Children.Add(new TextBlock { Text = name });
                    var item = new ComboBoxItem { Content = content };
                    System.Windows.Automation.AutomationProperties.SetName(item, name);
                    return item;
                }

                ComboBoxItem selected = null;
                foreach (var (name, color) in HighlightColors())
                {
                    var item = Item(name, new Border { Background = new SolidColorBrush(color) });
                    item.Tag = color;
                    list.Items.Add(item);
                    if (color == saved)
                        selected = item;
                }
                customItem = Item("Personalizada…", customSwatch);
                list.Items.Add(customItem);

                // A colour that is not in the list (transparency included) shows as "Personalizada…"
                if (selected == null)
                    customBox.Text = $"#{saved.R:X2}{saved.G:X2}{saved.B:X2}";
                list.SelectedItem = selected ?? customItem;
                UpdateCustomSwatch();

                list.SelectionChanged += (s, e) => UpdateCustomBox();
                list.IsVisibleChanged += (s, e) => UpdateCustomBox();
                customBox.TextChanged += (s, e) => UpdateCustomSwatch();
                customBox.LostFocus += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(customBox.Text))
                        ShowCustomError();
                };
            }

            // The chosen colour; a typed colour that is not valid keeps the saved one
            public Color Color => list.SelectedItem == customItem
                ? CustomColor() ?? saved
                : (Color)((ComboBoxItem)list.SelectedItem).Tag;

            // The colour to save ("Salvar"), saying why when a typed colour can't be used
            public Color Save()
            {
                ShowCustomError();
                saved = Color;
                return saved;
            }

            // "#RRGGBB" or "RRGGBB", see-through like the ready colours. The saved colour typed again keeps its own transparency.
            private Color? CustomColor()
            {
                string hex = customBox.Text.Trim();
                if (hex.StartsWith('#'))
                    hex = hex.Substring(1);
                if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rgb))
                    return null;

                var color = Color.FromArgb(HighlightAlpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return color.R == saved.R && color.G == saved.G && color.B == saved.B ? saved : color;
            }

            // The box shows only with "Personalizada…" chosen and the list on screen
            private void UpdateCustomBox()
            {
                bool custom = list.IsVisible && list.SelectedItem == customItem;
                customBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
                if (!custom)
                    customError.Visibility = Visibility.Collapsed;
            }

            // The "Personalizada…" sample shows the typed colour, or an empty outline
            private void UpdateCustomSwatch()
            {
                Color? color = CustomColor();
                customSwatch.Background = color is Color c ? new SolidColorBrush(c) : null;
                customSwatch.BorderThickness = new Thickness(color == null ? 1 : 0);
                if (color != null)
                    customError.Visibility = Visibility.Collapsed;
            }

            private void ShowCustomError()
            {
                customError.Visibility = customBox.IsVisible && CustomColor() == null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
