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
        /// Full path of browser\index.html (the welcome page).
        /// </summary>
        public static string GetIndexHtmlPath()
        {
            return Path.Combine(SourceBrowserPath, "index.html");
        }
    }
}
