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
        var s = App.Opcoes;

        return (ChatTypes)s.TipoDeChat switch
        {
            ChatTypes.Padrao => string.Join("|", s.Canal, s.Tema),
            ChatTypes.TwitchPopout => string.Join("|", s.Canal, s.BetterTtv, s.Emotes7tv, s.MenuDeEmotesDoBetterTtv,
                                                  s.FrankerFaceZ, s.AparenciaPadraoNoChatOficial, s.CssDoChatOficial),
            ChatTypes.CustomURL => string.Join("|", s.EnderecoPersonalizado, s.CssDoEnderecoPersonalizado),
            _ => s.TipoDeChat.ToString()
        };
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
        if (_currentChat?.ChatType != ChatTypes.Padrao)
            return true;

        if (_chatNavigationPending || this.webView?.CoreWebView2 == null)
            return false;

        // The filters, sound and "Apagar mensagens antigas" are read by the page on every message (see browser/chat.js)
        string script = $$"""
            (function (messages, css) {
                if (!window.oncChat) return false;
                window.oncChat.apply(messages);

                var style = document.getElementById('{{CustomCssElementId}}');
                if (!style) {
                    style = document.createElement('style');
                    style.id = '{{CustomCssElementId}}';
                    document.head.appendChild(style);
                }
                style.textContent = css;
                return true;
            })({{Chats.PadraoChat.GetMessageSettingsJson()}}, {{JsonSerializer.Serialize(_currentChat.SetupCustomCSS() ?? string.Empty)}});
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
