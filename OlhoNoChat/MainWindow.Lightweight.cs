namespace OlhoNoChat;

using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

/// <summary>
/// Keeps the embedded browser as light as possible while the chat sits on top of a game.
/// </summary>
public partial class MainWindow
{
    // Tracking/analytics scripts loaded by the KapChat page; the chat doesn't need them.
    private static readonly string[] BlockedRequestPatterns =
    {
        "*://*.google-analytics.com/*",
        "*://*.googletagmanager.com/*",
    };

    private void ApplyLightweightWebViewSettings(CoreWebView2 coreWebView2)
    {
        try
        {
            coreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

            foreach (string pattern in BlockedRequestPatterns)
                coreWebView2.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);

            coreWebView2.WebResourceRequested += (s, e) =>
            {
                e.Response = coreWebView2.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply lightweight WebView2 settings.");
        }
    }
}
