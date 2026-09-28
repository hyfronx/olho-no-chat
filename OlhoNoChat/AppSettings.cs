using OlhoNoChat.Atalhos;
using Jot;
using Jot.Storage;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace OlhoNoChat;

public class AppSettings
{
    public Tracker Tracker;
    public GeneralSettings GeneralSettings { get; set; }

    private readonly string _userDataFolder;

    private bool _isInitialized = false;

    public void Init()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        this.Tracker.Configure(this)
            .Properties<AppSettings>(w => new { w.GeneralSettings })
            .Id(w => w.GetType().Name, null, false);

        Application.Current.Exit += (s, e) => {
            this.Tracker.Persist(this);
            this.Tracker.StopTracking(this);
        };

        this.Tracker.Track(this);
        KeepChannelFromOldSettings();

        // Only "Nenhum" (0) and "Padrão" (1) exist (the other themes were hidden in 1.0.16 and later removed):
        // any other saved value becomes "Padrão"
        if (GeneralSettings.ThemeIndex != 0 && GeneralSettings.ThemeIndex != GeneralSettings.DefaultThemeIndex)
        {
            GeneralSettings.ThemeIndex = GeneralSettings.DefaultThemeIndex;
            this.Tracker.Persist(this);
        }

        // jCyan was removed: a saved chat type that no longer exists (3 = jCyan) becomes "Padrão"
        if (!Enum.IsDefined(typeof(ChatTypes), GeneralSettings.ChatType))
        {
            GeneralSettings.ChatType = (int)ChatTypes.Padrao;
            this.Tracker.Persist(this);
        }

        // The "Alert" sounds that came with the app were replaced by new ones after 1.2.0: a saved one becomes the new default
        // (only in the app's own folder; a chosen folder may have its own copies)
        if (GeneralSettings.SoundClipsFolder == "Default" && GeneralSettings.ChatNotificationSound.StartsWith("Alert "))
        {
            GeneralSettings.ChatNotificationSound = GeneralSettings.DefaultChatNotificationSound;
            this.Tracker.Persist(this);
        }
    }

    public AppSettings()
    {
        _userDataFolder = InfoDoApp.EhPortatil ? Path.Combine(AppContext.BaseDirectory, "settings") : InfoDoApp.PastaDeDados;

        SetAsideSettingsOfOldVersions(_userDataFolder);
        Tracker = new Tracker(new JsonFileStore(_userDataFolder));

        // Defaults are within the class constructor
        this.GeneralSettings = new GeneralSettings();
    }

    // Settings saved by versions before 1.0.17 (e.g. the 1.0.1 some friends installed) belong to a very
    // different app: updating to 1.1.0 starts them over with the values of a first install, keeping only
    // the Twitch channel. Recognized by a value every version since 1.0.17 saves (the "write" hotkey).
    // The old files are moved to a subfolder, not deleted.
    private const string OldSettingsFolderName = "Configurações antigas (antes da 1.1.0)";
    private string _channelFromOldSettings;

    private void SetAsideSettingsOfOldVersions(string folder)
    {
        try
        {
            string file = Path.Combine(folder, "AppSettings.json");
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file)) is not JsonArray items)
                return;

            var general = items.OfType<JsonObject>()
                .Select(i => i["Value"] as JsonObject)
                .FirstOrDefault(v => v != null && v.ContainsKey("Username"));
            if (general == null || general.ContainsKey(nameof(GeneralSettings.WriteMessageHotkey)))
                return; // 1.0.17 or newer: kept as it is

            _channelFromOldSettings = general["Username"]?.GetValue<string>() ?? string.Empty;

            string oldFolder = Path.Combine(folder, OldSettingsFolderName);
            Directory.CreateDirectory(oldFolder);
            foreach (string json in Directory.GetFiles(folder, "*.json"))
                File.Move(json, Path.Combine(oldFolder, Path.GetFileName(json)), overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not check the settings of an older version: {ex.Message}");
        }
    }

    // After the old settings were set aside: the channel they had, as a name (or none if it wasn't one)
    private void KeepChannelFromOldSettings()
    {
        if (_channelFromOldSettings == null)
            return;

        string channel = Twitch.NomesDaTwitch.Extrair(_channelFromOldSettings);
        GeneralSettings.Username = Twitch.NomesDaTwitch.EhValido(channel) ? channel.ToLowerInvariant() : string.Empty;
        _channelFromOldSettings = null;
        this.Tracker.Persist(this);
    }

    public void Persist()
    {
        this.Tracker.Persist(this);
    }

    // "Restaurar tudo para o padrão" (Configurações > Geral > Avançado): every option goes back to the value
    // of a first install. What isn't an option stays: the channel, the connected Twitch account and the name
    // lists of the chat filters. A copy of the settings file is kept in a subfolder first.
    private const string BeforeResetFolderName = "Configurações antes de restaurar o padrão";
    private static readonly HashSet<string> KeptOnReset = new()
    {
        nameof(GeneralSettings.Username),
        nameof(GeneralSettings.ChannelID), nameof(GeneralSettings.OAuthToken),
        nameof(GeneralSettings.TwitchLogin), nameof(GeneralSettings.TwitchDisplayName),
        nameof(GeneralSettings.AllowedUsersList), nameof(GeneralSettings.BlockedUsersList),
    };

    public void ResetToDefaults()
    {
        try
        {
            string file = Path.Combine(_userDataFolder, "AppSettings.json");
            if (File.Exists(file))
            {
                string copyFolder = Path.Combine(_userDataFolder, BeforeResetFolderName);
                Directory.CreateDirectory(copyFolder);
                File.Copy(file, Path.Combine(copyFolder, "AppSettings.json"), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not keep a copy of the settings: {ex.Message}");
        }

        var defaults = new GeneralSettings();
        foreach (var property in typeof(GeneralSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite && !KeptOnReset.Contains(property.Name))
                property.SetValue(GeneralSettings, property.GetValue(defaults));
        }
        Persist();
    }
}

public class GeneralSettings
{
    public string Username { get; set; } = string.Empty;
    public bool FadeChat { get; set; } = false;
    public string FadeTime { get; set; } = "120"; // seconds before old messages disappear (when FadeChat is on)
    public bool BlockBotActivity { get; set; } = true;
    public bool HideGifs { get; set; } = false; // GIFs of the "Padrão" chat are shown (only their title when on)
    public bool HideOtherChannels { get; set; } = false; // shared chat: the other channels' messages are shown
    // File name in the sounds folder, or "None"; first install: "Job done"
    public const string DefaultChatNotificationSound = "job-done.wav";
    public string ChatNotificationSound { get; set; } = DefaultChatNotificationSound;
    // 0 = sound on every new message; otherwise at most once every this many seconds (the first message always rings)
    public int ChatSoundQuietSeconds { get; set; } = 0;
    // Index in the Tema list of the Aparência tab: 0 = "Nenhum", 1 = "Padrão" (body.tema-padrao in browser/chat.css)
    public const int DefaultThemeIndex = 1;
    public int ThemeIndex { get; set; } = DefaultThemeIndex;
    public string CustomCSS { get; set; } = string.Empty;
    public string TwitchPopoutCSS { get; set; } = string.Empty;
    public bool UseDefaultTwitchPopoutCSS { get; set; } = true;
    public int ChatType { get; set; } = 0;
    public string CustomURL { get; set; } = string.Empty;
    // First install (and the "voltar ao padrão" value buttons): text at 80%, dark background at 65%
    // (the background buttons move it in steps of 15 out of 255: 165 is shown as 65%)
    public const double DefaultZoomLevel = 0.8;
    public const byte DefaultOpacityLevel = 165;
    public double ZoomLevel { get; set; } = DefaultZoomLevel;
    public byte OpacityLevel { get; set; } = DefaultOpacityLevel;
    public bool AutoHideBorders { get; set; } = false;
    public bool HideTaskbarIcon { get; set; } = false;
    public bool AllowInteraction { get; set; } = true;
    public bool HighlightUsersChat { get; set; } = false;
    public bool AllowedUsersOnlyChat { get; set; } = false;
    public bool FilterAllowAllMods { get; set; } = false;
    public bool FilterAllowAllVIPs { get; set; } = false;
    public StringCollection AllowedUsersList { get; set; } = new StringCollection();
    public StringCollection BlockedUsersList { get; set; } = new StringCollection();
    public bool RedemptionsEnabled { get; set; } = false;
    // Twitch account connected in the Twitch tab: ChannelID is its user id (the redemptions come from
    // that channel), OAuthToken the access given in the user's browser
    public string ChannelID { get; set; } = string.Empty;
    public string OAuthToken { get; set; } = string.Empty;
    public string TwitchLogin { get; set; } = string.Empty;
    public string TwitchDisplayName { get; set; } = string.Empty;
    // Typing box of the "Chat oficial da Twitch": false = the app's "Escrever no chat…" box (uses the
    // account above), true = Twitch's own box (needs a login to twitch.tv inside the chat window)
    public bool UseTwitchChatBox { get; set; } = false;
    // The box opened with the hotkey closes (and the game comes back to the front) right after sending.
    // Off: it stays open until the hotkey, Esc or its "x" (since 1.0.19; before, it always closed).
    public bool CloseChatBoxAfterSend { get; set; } = false;
    public bool BetterTtv { get; set; } = true;
    public bool BetterTtv_7tv { get; set; } = true;
    public bool BetterTtv_AdvEmoteMenu { get; set; } = true;
    public bool FrankerFaceZ { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    // Message text look ("Padrão" chat, also the official one). Empty / "theme" keep what the chat theme defines.
    public string ChatMessageColor { get; set; } = string.Empty; // "#RRGGBB"
    public string ChatTextOutline { get; set; } = "none";        // theme (black outline of the theme) | soft | none
    public string ChatFontFamily { get; set; } = "theme";        // theme | Segoe UI | Arial | Verdana
    public bool ShowMessageTime { get; set; } = false;           // time each message arrived, before the name

    public Color ChatHighlightColor { get; set; } = Color.FromArgb(150, 245, 245, 0); // Yellow
    public Color ChatHighlightModsColor { get; set; } = Color.FromArgb(150, 0, 173, 3); // Green
    public Color ChatHighlightVIPsColor { get; set; } = Color.FromArgb(150, 219, 51, 179); // Purple
    public float OutputVolume { get; set; } = 1.0f;
    public string DeviceName { get; set; } = string.Empty;
    public int DeviceID { get; set; } = -1;
    public string SoundClipsFolder { get; set; } = "Default";
    public Atalho ToggleBordersHotkey { get; set; } = new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Alt);
    public Atalho ToggleInteractableHotkey { get; set; } = new Atalho(Key.F7, ModifierKeys.Control | ModifierKeys.Alt);
    // "Sempre no topo" (pin of the title bar): the chat stays in front of the game and every other window.
    // Off: a normal window. BringToTopHotkey (named after the "Trazer para a frente" it used to be) switches it too.
    public bool AlwaysOnTop { get; set; } = true;
    public Atalho BringToTopHotkey { get; set; } = new Atalho(Key.F8, ModifierKeys.Control | ModifierKeys.Alt);
    public Atalho WriteMessageHotkey { get; set; } = new Atalho(Key.F11, ModifierKeys.Control | ModifierKeys.Alt); // F10: Alt+F10 is a common game-capture shortcut
    public bool AllowMultipleInstances { get; set; } = false;
}
