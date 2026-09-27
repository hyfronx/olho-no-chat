namespace OlhoNoChat;

using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Saving the settings used to load the chat page again every time, which clears the messages
/// on screen. Now the page is only loaded again for settings it reads once when it opens
/// (channel, theme, chat type...). Everything else is applied to the open page.
/// </summary>
public partial class MainWindow
{
    // Id of the style element with our CSS (theme "Nenhum" CSS, highlight colors, message text options)
    private const string CustomCssElementId = "onc-custom-css";

    // Settings the chat page only reads when it is loaded. If any of them changes, saving loads it again.
    private static string GetChatReloadKey()
    {
        var s = App.Settings.GeneralSettings;

        return (ChatTypes)s.ChatType switch
        {
            ChatTypes.KapChat => string.Join("|", s.Username, s.ThemeIndex),
            ChatTypes.TwitchPopout => string.Join("|", s.Username, s.BetterTtv, s.BetterTtv_7tv, s.BetterTtv_AdvEmoteMenu,
                                                  s.FrankerFaceZ, s.UseDefaultTwitchPopoutCSS, s.TwitchPopoutCSS),
            ChatTypes.CustomURL => string.Join("|", s.CustomURL, s.CustomCSS),
            _ => s.ChatType.ToString()
        };
    }

    // KapChat "fade" address parameter: seconds before old messages disappear, or "false"
    private static string GetKapChatFadeParam()
    {
        return App.Settings.GeneralSettings.FadeChat ? App.Settings.GeneralSettings.FadeTime : "false";
    }

    /// <summary>
    /// Applies the saved settings to the open chat page, keeping its messages.
    /// Returns false when that isn't possible (page still loading or not working), so the caller
    /// loads the page again as before.
    /// </summary>
    private async Task<bool> TryApplyChatSettingsLiveAsync()
    {
        // "Chat oficial da Twitch": only our CSS changes live (the typing box to show)
        if (_currentChat?.ChatType == ChatTypes.TwitchPopout && this.webView?.CoreWebView2 != null && !_chatNavigationPending)
        {
            _ = this.webView.CoreWebView2.ExecuteScriptAsync(InsertCustomCSS2(_currentChat.SetupCustomCSS()));
            return true;
        }

        // The other chat types have nothing to update live: all their options are in the reload key.
        if (_currentChat?.ChatType != ChatTypes.KapChat)
            return true;

        if (_chatNavigationPending || this.webView?.CoreWebView2 == null)
            return false;

        var settings = App.Settings.GeneralSettings;
        string pageSettings = JsonSerializer.Serialize(new
        {
            fade = GetKapChatFadeParam(),
            botActivity = (!settings.BlockBotActivity).ToString(),
            css = _currentChat.SetupCustomCSS() ?? string.Empty
        });

        // KapChat reads "fade" and "bot_activity" from the page address on every message, and the
        // fade time from Chat.vars; the filters are read from window.oncChatSettings (see KapChat.cs).
        string script = $$"""
            (function (page, messages) {
                if (typeof Chat === 'undefined' || !Chat.vars || !window.oncChatSettings) return false;

                var params = new URLSearchParams(location.search);
                params.set('fade', page.fade);
                params.set('bot_activity', page.botActivity);
                history.replaceState(history.state, '', location.pathname + '?' + params.toString() + location.hash);
                Chat.vars.maxDisplayTime = page.fade === 'true' ? 30 : parseInt(page.fade);

                Object.assign(window.oncChatSettings, messages);

                var css = document.getElementById('{{CustomCssElementId}}');
                if (!css) {
                    css = document.createElement('style');
                    css.id = '{{CustomCssElementId}}';
                    document.head.appendChild(css);
                }
                css.textContent = page.css;
                return true;
            })({{pageSettings}}, {{Chats.KapChat.GetMessageSettingsJson()}});
            """;

        try
        {
            string result = await this.webView.CoreWebView2.ExecuteScriptAsync(script);
            return result == "true";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply the settings to the open chat page; loading it again.");
            return false;
        }
    }
}
