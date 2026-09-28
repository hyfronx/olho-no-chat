using Microsoft.Web.WebView2.Core;

namespace OlhoNoChat;

public static class WebView2EnvironmentManager
{
    private static Task<CoreWebView2Environment> _environmentTask;

    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        // If the task is null, create it. This ensures the environment is only created once.
        if (_environmentTask == null)
        {
            var options = new CoreWebView2EnvironmentOptions()
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --msWebView2CancelInitialNavigation"
                    // Background browser services a chat overlay never uses
                    + " --disable-background-networking --disable-component-update --disable-extensions --disable-sync --no-first-run"
                    + " --disable-features=Translate,msEdgeTranslate,OptimizationHints,AutofillServerCommunication,MediaRouter"
            };

            string userDataFolder = InfoDoApp.PastaDeDados;
            _environmentTask = CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
        }

        return _environmentTask;
    }

    // After the browser process ended: the next WebView2 starts on a new environment (also when the cached one failed)
    public static void Reset() => _environmentTask = null;
}