namespace OlhoNoChat;

using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

/// <summary>
/// Keeps the embedded browser as light as possible while the chat sits on top of a game.
/// </summary>
public partial class MainWindow
{
    private void ApplyLightweightWebViewSettings(CoreWebView2 coreWebView2)
    {
        try
        {
            coreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply lightweight WebView2 settings.");
        }
    }
}
