using System.Text.RegularExpressions;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Channel and user names typed by the user: "nome", "@nome", "twitch.tv/nome" or a link copied from
/// the browser ("https://www.twitch.tv/nome/videos", "twitch.tv/popout/nome/chat?popout=").
/// </summary>
public static class TwitchNames
{
    // Twitch login names: letters, numbers and underscore
    private static readonly Regex Login = new Regex("^[A-Za-z0-9_]{1,25}$");

    // Addresses where the channel comes after one more part (twitch.tv/popout/nome/chat)
    private static readonly string[] PagesBeforeChannel = { "popout", "moderator", "embed" };

    /// <summary>The name inside what was typed ("" when nothing was typed). It may still be invalid: see <see cref="IsValid"/>.</summary>
    public static string Extract(string text)
    {
        string name = (text ?? string.Empty).Trim();

        int host = name.IndexOf("twitch.tv", StringComparison.OrdinalIgnoreCase);
        if (host >= 0)
        {
            string path = name.Substring(host + "twitch.tv".Length).Split('?', '#')[0];
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int index = parts.Length > 1 && PagesBeforeChannel.Contains(parts[0], StringComparer.OrdinalIgnoreCase) ? 1 : 0;
            name = parts.Length > index ? parts[index] : string.Empty;
        }
        else
        {
            name = name.TrimEnd('/');
            if (name.Contains('/'))
                name = name.Substring(name.LastIndexOf('/') + 1);
        }

        return name.Trim().TrimStart('@');
    }

    public static bool IsValid(string name) => name != null && Login.IsMatch(name);

    /// <summary>Why a name that isn't empty can't be used (shown under the box).</summary>
    public const string InvalidNameHint = "Use o nome como aparece no endereço do canal (twitch.tv/nome): só letras, números e _.";
}
