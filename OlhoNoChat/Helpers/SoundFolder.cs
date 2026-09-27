using System.IO;

namespace OlhoNoChat.Helpers;

internal static class SoundFolder
{
    // "Default", or a chosen folder that no longer exists: the sounds that come with the app
    public static string Resolve(string setting)
    {
        return setting != "Default" && Directory.Exists(setting)
            ? setting
            : Path.Combine(AppContext.BaseDirectory, "assets");
    }
}
