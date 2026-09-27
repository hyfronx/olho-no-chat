using System.IO;
using NuGet.Versioning;
using Velopack.Locators;

namespace OlhoNoChat;

public static class AppInfo
{
#if DEBUG
    // Development builds keep their own settings (and single-instance pipe, see IpcManager),
    // so they can be tested while the installed app keeps running.
    public const string DataFolderName = "OlhoNoChat-Dev";
#else
    public const string DataFolderName = "OlhoNoChat";
#endif
    public const string RepositoryUrl = "https://github.com/hyfronx/olho-no-chat";

    // "Olho no Chat" application registered at dev.twitch.tv (confidential client, OAuth redirect
    // http://localhost:8981/auth). "Conectar" in the Twitch tab authorizes it in the user's browser.
    public const string TwitchClientId = "zrqsilh31pbdlfjb81onhulkvzytgh";

    // What "Conectar" asks for: writing in the chat, reading the channel point redemptions and the list
    // of emotes the account can use (emote button of the message box, since 1.0.18)
    public const string TwitchScopes = "user:write:chat channel:read:redemptions user:read:emotes";

    public static string UserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);

    public static string Version {
        get 
        {
            IVelopackLocator locator = VelopackLocator.Current;
            SemanticVersion currentVersion = locator.CurrentlyInstalledVersion;

            if (currentVersion != null)
            {
                return currentVersion.ToString();
            }
            else
            {
                return "de desenvolvimento";
            }
        } 
    }

    public static bool IsPortable {
        get {
            IVelopackLocator locator = VelopackLocator.Current;
            if (locator == null)
            {
                return false;
            }
            return locator.IsPortable;
        }
    }
}
