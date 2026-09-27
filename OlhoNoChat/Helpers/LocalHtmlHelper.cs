using System.IO;

namespace OlhoNoChat.Helpers
{
    /// <summary>
    /// Paths of the local pages bundled with the app (the "browser" folder next to the .exe).
    /// </summary>
    internal static class LocalHtmlHelper
    {
        private static readonly string SourceBrowserPath = Path.Combine(
            AppContext.BaseDirectory, "browser");

        /// <summary>
        /// Host name under which the WebView2 serves the "browser" folder (see MainWindow.SetupWebViewAsync):
        /// the chat page gets a real https address, so it can load emotes from other sites.
        /// ".example" is reserved, so it never is a real site.
        /// </summary>
        public const string ChatPageHost = "olhonochat.example";

        public static string BrowserFolder => SourceBrowserPath;

        /// <summary>
        /// Full path of browser\index.html (the welcome page).
        /// </summary>
        public static string GetIndexHtmlPath()
        {
            return Path.Combine(SourceBrowserPath, "index.html");
        }

        /// <summary>
        /// Address of the "Padrão" chat page (browser\chat.html) for a channel; darkTheme = theme "Padrão".
        /// </summary>
        public static string GetChatPageUrl(string channel, bool darkTheme)
        {
            string url = $"https://{ChatPageHost}/chat.html?canal={Uri.EscapeDataString(channel)}";
            return darkTheme ? url + "&tema=padrao" : url;
        }
    }
}
