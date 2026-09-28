using OlhoNoChat.Som;
using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Som" tab: new-message sound, output device, volume and the folder the sounds come from.
/// </summary>
public partial class SoundSettingsPage : UserControl
{
    // "Default" is the value stored in the settings; only the label is translated.
    private string _soundClipsFolder = SonsDisponiveis.PastaPadrao;

    public SoundSettingsPage()
    {
        InitializeComponent();
    }

    public void SetupValues()
    {
        LoadDevices();
        this.OutputVolumeSlider.Value = App.Opcoes.Volume * 100;

        SetSoundClipsFolder(App.Opcoes.PastaDosSons);
        LoadSoundClips();
        SelectSound(App.Opcoes.SomDeMensagem);
        SelectQuietSeconds(App.Opcoes.SegundosEntreSons);

        if (Enum.IsDefined(typeof(ChatTypes), App.Opcoes.TipoDeChat))
            ChatTypeChanged((ChatTypes)App.Opcoes.TipoDeChat);
    }

    public void SaveValues()
    {
        var saida = DevicesComboBox.SelectedItem as TocadorDeAviso.Saida;
        App.Opcoes.SaidaDeSom = saida?.Id ?? TocadorDeAviso.Padrao;
        App.Opcoes.NomeDaSaidaDeSom = saida == null || saida.Id == TocadorDeAviso.Padrao
            ? TocadorDeAviso.NomeDaPadraoGravado
            : saida.Nome;

        App.Opcoes.Volume = (float)SliderToVolume(this.OutputVolumeSlider.Value);
        App.Opcoes.PastaDosSons = _soundClipsFolder;
        App.Opcoes.SegundosEntreSons =
            int.TryParse((comboSoundQuiet.SelectedItem as ComboBoxItem)?.Tag as string, out int quiet) ? quiet : 0;

        // The other chat types have no new-message sound, so their saved choice is left alone
        var chatType = (ChatTypes)App.Opcoes.TipoDeChat;
        if (chatType == ChatTypes.Padrao)
            App.Opcoes.SomDeMensagem = this.comboChatSound.SelectedValue.ToString();
    }

    public void ChatTypeChanged(ChatTypes chatType)
    {
        bool hasSound = chatType == ChatTypes.Padrao;
        chatSoundCard.Visibility = hasSound ? Visibility.Visible : Visibility.Collapsed;
        chatSoundUnavailable.Visibility = hasSound ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectQuietSeconds(int seconds)
    {
        var item = comboSoundQuiet.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == seconds.ToString());
        if (item == null)
        {
            // A value that isn't in the list (edited by hand): keep it as an extra option
            item = new ComboBoxItem { Tag = seconds.ToString(), Content = $"No máximo 1 vez a cada {seconds} s" };
            comboSoundQuiet.Items.Add(item);
        }
        comboSoundQuiet.SelectedItem = item;
    }

    private static double SliderToVolume(double value)
    {
        double s = Math.Round((value * 0.01), 2);
        return Math.Max(0, Math.Min(1, s));
    }

    private void LoadDevices()
    {
        // Loaded again by "Restaurar tudo para o padrão"
        DevicesComboBox.DisplayMemberPath = nameof(TocadorDeAviso.Saida.Nome);
        DevicesComboBox.SelectedValuePath = nameof(TocadorDeAviso.Saida.Id);
        DevicesComboBox.ItemsSource = TocadorDeAviso.ListarSaidas();

        // Um aparelho que não é mais o mesmo (outro foi ligado ou tirado) aparece como "Padrão do Windows"
        var settings = App.Opcoes;
        DevicesComboBox.SelectedValue = TocadorDeAviso.SaidaAindaExiste(settings.SaidaDeSom, settings.NomeDaSaidaDeSom ?? string.Empty)
            ? settings.SaidaDeSom
            : TocadorDeAviso.Padrao;
    }

    private void SetSoundClipsFolder(string folder)
    {
        _soundClipsFolder = string.IsNullOrEmpty(folder) ? SonsDisponiveis.PastaPadrao : folder;
        tbSoundClipsFolder.Text = _soundClipsFolder == SonsDisponiveis.PastaPadrao ? "Padrão (sons que vêm com o app)" : _soundClipsFolder;
    }

    private void LoadSoundClips()
    {
        comboChatSound.Items.Clear();
        comboChatSound.Items.Add(new ComboBoxItem() { Content = "Nenhum", Tag = SonsDisponiveis.Nenhum });
        comboChatSound.SelectedIndex = 0;

        foreach (SonsDisponiveis.Som som in SonsDisponiveis.Listar(SonsDisponiveis.ResolverPasta(_soundClipsFolder)))
            comboChatSound.Items.Add(new ComboBoxItem() { Content = som.Nome, Tag = som.Arquivo });
    }

    private void SelectSound(string fileName)
    {
        var item = comboChatSound.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == fileName);
        comboChatSound.SelectedIndex = item == null ? 0 : comboChatSound.Items.IndexOf(item);
    }

    // Plays the chosen sound on the chosen output, at the volume on the slider (not saved yet)
    private void PlaySelectedSound()
    {
        TocadorDeAviso.Previa(SonsDisponiveis.Caminho(_soundClipsFolder, this.comboChatSound.SelectedValue?.ToString()),
            (float)SliderToVolume(this.OutputVolumeSlider.Value),
            DevicesComboBox.SelectedValue is int deviceId ? deviceId : TocadorDeAviso.Padrao);
    }

    // --- Event Handlers --------------------------------------------------------------------------------

    private void comboChatSound_DropDownClosed(object sender, EventArgs e)
    {
        PlaySelectedSound();
    }

    private void btPlaySound_Click(object sender, RoutedEventArgs e)
    {
        PlaySelectedSound();
    }

    private void btChangeSoundClipsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            ChangeSoundClipsFolder(dialog.FolderName);
    }

    private void btDefaultSoundClipsFolder_Click(object sender, RoutedEventArgs e)
    {
        ChangeSoundClipsFolder(SonsDisponiveis.PastaPadrao);
    }

    private void ChangeSoundClipsFolder(string folder)
    {
        string current = this.comboChatSound.SelectedValue?.ToString();

        SetSoundClipsFolder(folder);
        LoadSoundClips();

        // Keep the chosen sound if the new folder has a file with the same name
        SelectSound(current);
    }
}
