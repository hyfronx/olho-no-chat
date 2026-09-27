using NAudio;
using NAudio.Wave;
using System.Diagnostics;
using System.Windows;

namespace OlhoNoChat.Utils;

/// <summary>
/// The new-message sound of the "Padrão" chat. The page asks for it on every message that may ring
/// (onc:play-sound, see browser/chat.js); Play decides if it really rings.
/// </summary>
public class ChatSoundPlayer
{
    private string _mediaFile;
    private AudioFileReader _audioFileReader;
    private WaveOutEvent _waveOutDevice;
    private bool _initializationFailed = false;

    // When the sound last played (see ChatSoundQuietSeconds)
    private DateTime _lastSoundPlayed = DateTime.MinValue;

    public void SetMediaFile(string file)
    {
        _mediaFile = file;
        LoadSoundFile();
    }

    public void OnAudioDeviceChanged()
    {
        // Dispose of the old device, as its configuration is now stale.
        DisposeDevice();

        // Reset the flag to allow a new initialization attempt on the next Play call.
        _initializationFailed = false;
    }

    /// <summary>
    /// Loads the audio file into the reader and sets its volume.
    /// This cleans up previous resources before loading the new file.
    /// </summary>
    private void LoadSoundFile()
    {
        // Dispose of the previous file reader if it exists
        _audioFileReader?.Dispose();
        _audioFileReader = null;

        if (string.IsNullOrEmpty(_mediaFile) || _mediaFile.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return; // No file to load
        }

        try
        {
            _audioFileReader = new AudioFileReader(_mediaFile);
            _audioFileReader.Volume = App.Settings.GeneralSettings.OutputVolume;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar o arquivo de som: {_mediaFile}\n\n{ex.Message}", "Erro ao carregar som", MessageBoxButton.OK, MessageBoxImage.Error);
            _audioFileReader = null; // Ensure reader is null if loading fails
        }
    }

    /// <summary>
    /// Ensures the audio output device is initialized. This is called only when playback is requested.
    /// </summary>
    private bool EnsureDeviceInitialized()
    {
        // If the device is already created and initialized, we're good to go.
        if (_waveOutDevice != null) return true;

        // If there's no audio file loaded, we can't initialize.
        if (_audioFileReader == null) return false;

        try
        {
            // Check for available devices first.
            if (WaveOut.DeviceCount == 0)
            {
                Debug.WriteLine("No audio output devices were found on this system.");
                return false;
            }

            // Create the output device instance.
            _waveOutDevice = new WaveOutEvent();

            // Verify the user's selected device is still valid.
            var deviceId = App.Settings.GeneralSettings.DeviceID;

            // If the ID is negative, it's 'Default'. No further checks needed.
            if (deviceId < 0)
            {
                _waveOutDevice.DeviceNumber = -1;
            }
            // If the ID is out of range OR the name no longer matches, reset to default.
            else if (deviceId >= WaveOut.DeviceCount)
            {
                Debug.WriteLine("The previously selected audio device could not be found. Reverting to the default device.");
                App.Settings.GeneralSettings.DeviceID = -1;
                App.Settings.GeneralSettings.DeviceName = "Default";
                _waveOutDevice.DeviceNumber = -1;
            }
            else
            {
                var capabilities = WaveOut.GetCapabilities(deviceId);
                if (!App.Settings.GeneralSettings.DeviceName.StartsWith(capabilities.ProductName))
                {
                    Debug.WriteLine($"The audio device mapping has changed. The device at index {deviceId} is now '{capabilities.ProductName}'. Reverting to the default device.");
                    App.Settings.GeneralSettings.DeviceID = -1;
                    App.Settings.GeneralSettings.DeviceName = "Default";
                    _waveOutDevice.DeviceNumber = -1;
                }
                else
                {
                    // The device ID is valid and the name matches. Use it.
                    _waveOutDevice.DeviceNumber = deviceId;
                }
            }

            // Finally, initialize the device with the audio file.
            _waveOutDevice.Init(_audioFileReader);

            return true;
        }
        catch (MmException ex) when (ex.Result == MmResult.BadDeviceId)
        {
            _initializationFailed = true;
            Debug.WriteLine("The selected audio device is invalid or unavailable. Falling back to the default device.");
            DisposeDevice();
            App.Settings.GeneralSettings.DeviceID = -1;
            App.Settings.GeneralSettings.DeviceName = "Default";
            return false;
        }
        catch (Exception ex)
        {
            _initializationFailed = true;
            Debug.WriteLine($"An unexpected error occurred while initializing the audio device. {ex.Message}");
            DisposeDevice();
            return false;
        }
    }

    public void Play()
    {
        // With a time set, the first message rings and the next ones only once that long has passed since
        // the sound played: a busy chat rings at most once every so many seconds instead of once per message.
        DateTime now = DateTime.UtcNow;
        int quietSeconds = App.Settings.GeneralSettings.ChatSoundQuietSeconds;
        if (quietSeconds > 0 && (now - _lastSoundPlayed).TotalSeconds < quietSeconds)
        {
            return;
        }

        // If we've already tried and failed, don't hammer the system with more attempts.
        if (_initializationFailed)
        {
            return;
        }

        // Ensure the output device is ready (or try to initialize it).
        if (!EnsureDeviceInitialized())
            return;

        try
        {
            if (!string.IsNullOrEmpty(_mediaFile))
            {
                // A message that arrives while the sound is still playing doesn't ring: in a busy chat the
                // sound is neither restarted nor stacked, it just plays to the end
                if (_waveOutDevice.PlaybackState == PlaybackState.Playing)
                    return;

                _audioFileReader.Position = 0;
                _waveOutDevice.Play();
                _lastSoundPlayed = now;
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
    }

    /// <summary>
    /// Cleans up the audio output device.
    /// </summary>
    private void DisposeDevice()
    {
        _waveOutDevice?.Stop();
        _waveOutDevice?.Dispose();
        _waveOutDevice = null;
    }
}
