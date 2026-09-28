namespace OlhoNoChat;

using OlhoNoChat.Sistema;
using Microsoft.Web.WebView2.Core;

/// <summary>
/// Links in the chat messages ("Padrão": turned into links by browser/chat.js; "Chat oficial da
/// Twitch": Twitch's own) open in the user's browser, and only while the borders are visible: without
/// the borders the chat stays read-only, as always. Twitch's login page keeps opening inside the app
/// (the "Da própria Twitch" typing box needs a login there).
/// </summary>
public partial class MainWindow
{
    private void SetupChatLinks(CoreWebView2 coreWebView2)
    {
        coreWebView2.NewWindowRequested += (s, e) =>
        {
            if (IsTwitchLoginPage(e.Uri))
                return; // the app's own window, as before

            e.Handled = true; // never a browser window inside the app
            if (!_hiddenBorders && Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                AbrirNoWindows.Site(uri.AbsoluteUri);
            }
        };
    }

    private static bool IsTwitchLoginPage(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        string path = uri.AbsolutePath.ToLowerInvariant();
        return host is "id.twitch.tv" or "passport.twitch.tv"
            || (host is "www.twitch.tv" or "twitch.tv" && (path.StartsWith("/login") || path.StartsWith("/signup")));
    }

    // The "Padrão" chat shows its links as clickable only while the borders are visible
    private void UpdateChatLinks()
    {
        if (_currentChat?.ChatType == ChatTypes.Padrao && this.webView?.CoreWebView2 != null)
        {
            _ = this.webView.CoreWebView2.ExecuteScriptAsync(
                $"document.body && document.body.classList.toggle('onc-links-on', {(_hiddenBorders ? "false" : "true")});");
        }
    }
}
