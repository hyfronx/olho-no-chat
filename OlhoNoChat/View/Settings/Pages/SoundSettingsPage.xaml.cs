using NAudio.Wave;
using OlhoNoChat.Helpers;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Som" tab: new-message sound, output device, volume and the folder the sounds come from.
/// </summary>
public partial class SoundSettingsPage : UserControl
{
    // "Default" is the value stored in the settings; only the label is translated.
    private string _soundClipsFolder = "Default";

    public SoundSettingsPage()
    {
        InitializeComponent();
    }

    public void SetupValues()
    {
        LoadDevices();
        this.OutputVolumeSlider.Value = App.Settings.GeneralSettings.OutputVolume * 100;

        SetSoundClipsFolder(App.Settings.GeneralSettings.SoundClipsFolder);
        LoadSoundClips();
        SelectSound(App.Settings.GeneralSettings.ChatNotificationSound);
        SelectQuietSeconds(App.Settings.GeneralSettings.ChatSoundQuietSeconds);

        if (Enum.IsDefined(typeof(ChatTypes), App.Settings.GeneralSettings.ChatType))
            ChatTypeChanged((ChatTypes)App.Settings.GeneralSettings.ChatType);
    }

    public void SaveValues()
    {
        App.Settings.GeneralSettings.DeviceID = (int)DevicesComboBox.SelectedValue;
        App.Settings.GeneralSettings.DeviceName = App.Settings.GeneralSettings.DeviceID == -1 ? "Default" : DevicesComboBox.Text;

        App.Settings.GeneralSettings.OutputVolume = (float)SliderToVolume(this.OutputVolumeSlider.Value);
        App.Settings.GeneralSettings.SoundClipsFolder = _soundClipsFolder;
        App.Settings.GeneralSettings.ChatSoundQuietSeconds =
            int.TryParse((comboSoundQuiet.SelectedItem as ComboBoxItem)?.Tag as string, out int quiet) ? quiet : 0;

        // The other chat types have no new-message sound, so their saved choice is left alone
        var chatType = (ChatTypes)App.Settings.GeneralSettings.ChatType;
        if (chatType == ChatTypes.Padrao)
            App.Settings.GeneralSettings.ChatNotificationSound = this.comboChatSound.SelectedValue.ToString();
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
        DevicesComboBox.Items.Clear(); // loaded again by "Restaurar tudo para o padrão"
        DevicesComboBox.Items.Add(new { Id = -1, Name = "Padrão do Windows" });

        for (int deviceId = 0; deviceId < WaveOut.DeviceCount; deviceId++)
        {
            var capabilities = WaveOut.GetCapabilities(deviceId);
            DevicesComboBox.Items.Add(new { Id = deviceId, Name = capabilities.ProductName });
        }

        DevicesComboBox.DisplayMemberPath = "Name";
        DevicesComboBox.SelectedValuePath = "Id";

        DevicesComboBox.SelectedValue = App.Settings.GeneralSettings.DeviceID;
        if (App.Settings.GeneralSettings.DeviceID != -1 && !DevicesComboBox.Text.StartsWith(App.Settings.GeneralSettings.DeviceName))
        {
            DevicesComboBox.SelectedValue = -1;
        }
    }

    private void SetSoundClipsFolder(string folder)
    {
        _soundClipsFolder = string.IsNullOrEmpty(folder) ? "Default" : folder;
        tbSoundClipsFolder.Text = _soundClipsFolder == "Default" ? "Padrão (sons que vêm com o app)" : _soundClipsFolder;
    }

    private void LoadSoundClips()
    {
        comboChatSound.Items.Clear();
        comboChatSound.Items.Add(new ComboBoxItem() { Content = "Nenhum", Tag = "None" });
        comboChatSound.SelectedIndex = 0;

        string path = SoundFolder.Resolve(_soundClipsFolder);

        if (!Directory.Exists(path)) return;

        string[] filesWav = Directory.GetFiles(path, "*.wav");
        string[] filesMp3 = Directory.GetFiles(path, "*.mp3");

        foreach (string file in filesWav.Concat(filesMp3))
        {
            string fileName = Path.GetFileName(file);
            comboChatSound.Items.Add(new ComboBoxItem() { Content = GetSoundDisplayName(fileName), Tag = fileName });
        }
    }

    private void SelectSound(string fileName)
    {
        var item = comboChatSound.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == fileName);
        comboChatSound.SelectedIndex = item == null ? 0 : comboChatSound.Items.IndexOf(item);
    }

    // "job-done.wav" -> "Job done"
    private static string GetSoundDisplayName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ').Replace('_', ' ');
        return name.Length == 0 ? name : char.ToUpper(name[0]) + name.Substring(1);
    }

    // Plays the chosen sound on the chosen output, at the volume on the slider (not saved yet)
    private void PlaySelectedSound()
    {
        string file = Path.Combine(SoundFolder.Resolve(_soundClipsFolder), this.comboChatSound.SelectedValue?.ToString() ?? "None");
        if (!File.Exists(file))
            return;

        try
        {
            var audioFileReader = new AudioFileReader(file);
            audioFileReader.Volume = (float)SliderToVolume(this.OutputVolumeSlider.Value);

            var waveOutDevice = new WaveOutEvent();
            if (DevicesComboBox.SelectedValue is int deviceId && deviceId < WaveOut.DeviceCount)
                waveOutDevice.DeviceNumber = deviceId;

            waveOutDevice.Init(audioFileReader);
            waveOutDevice.PlaybackStopped += (s, e) =>
            {
                audioFileReader.Dispose();
                waveOutDevice.Dispose();
            };
            waveOutDevice.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to play '{file}': {ex.Message}");
        }
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
        ChangeSoundClipsFolder("Default");
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
