using System.Text.Json;
using Color = System.Windows.Media.Color;

namespace OlhoNoChat.Chats
{
    public class PadraoChat : Chat
    {
        public PadraoChat() : base(ChatTypes.Padrao)
        {
        }

        // Look of the messages in the "Padrão" chat (the theme gives the font: bold 18 px Helvetica/Arial).
        // The "Chat oficial da Twitch" copies it (see CustomURLChat), so both look the same.
        public const string MessageFontFamily = "\"Helvetica Neue\", Helvetica, Arial, sans-serif";
        public const int MessageFontSizePx = 18;
        public const int LineHeightPx = 24;   // between the lines of one message
        public const int MessageGapPx = 6;    // above and below each message

        // "Mostrar o horário das mensagens" (also used by the official chat)
        public const string MessageTimeCss =
            "display: inline !important; color: #A8A8A8 !important; font-size: 15px !important; font-weight: 600 !important; " +
            "letter-spacing: 0 !important; margin-right: 6px !important; padding: 0 !important;";

        // A line in the user's color, like a /me message (channel point redemptions)
        public override string PushNewChatMessage(string message, string nick, string color)
        {
            return $"window.oncChat && window.oncChat.addAction({JsonSerializer.Serialize(nick ?? string.Empty)}, " +
                   $"{JsonSerializer.Serialize(color)}, {JsonSerializer.Serialize(message)});";
        }

        // Settings read by the page (browser\chat.js) on every message. Saving the settings updates
        // them on the open page (window.oncChat.apply), without reloading the chat.
        public static string GetMessageSettingsJson()
        {
            var settings = App.Settings.GeneralSettings;

            var vipList = settings.AllowedUsersList?.Cast<string>().Select(u => u.ToLowerInvariant()).ToList() ?? new List<string>();
            var blockList = settings.BlockedUsersList?.Cast<string>().Select(u => u.ToLowerInvariant()).ToList() ?? new List<string>();

            // "Apagar mensagens antigas": seconds before a message disappears, 0 = never
            int fade = settings.FadeChat && int.TryParse(settings.FadeTime, out int seconds) && seconds > 0 ? seconds : 0;

            return JsonSerializer.Serialize(new
            {
                fade,
                hideBots = settings.BlockBotActivity,
                hideGifs = settings.HideGifs,
                highlightUsers = settings.HighlightUsersChat,
                allowedUsersOnly = settings.AllowedUsersOnlyChat,
                playSound = settings.ChatNotificationSound?.ToLower() != "none",
                filterAllowAllVIPs = settings.FilterAllowAllVIPs,
                filterAllowAllMods = settings.FilterAllowAllMods,
                vips = vipList,
                blockList = blockList
            });
        }

        // The page connects to the chat once it has the settings (see browser/chat.js)
        public override string SetupJavascript()
        {
            return $"window.oncChat && window.oncChat.start({GetMessageSettingsJson()});";
        }

        public override string SetupCustomCSS()
        {
            // Theme "Nenhum" with the user's own CSS: that CSS instead of ours. Only for that theme: the same
            // setting also keeps the CSS of the "Endereço personalizado" chat type, which must not end up here.
            if (App.Settings.GeneralSettings.ThemeIndex == 0 && !string.IsNullOrEmpty(App.Settings.GeneralSettings.CustomCSS))
            {
                return App.Settings.GeneralSettings.CustomCSS + GetMessageTextCss();
            }

            // Prepare the dynamic color values first. Invariant: CSS needs "0.59", not "0,59".
            Color highlightColor = App.Settings.GeneralSettings.ChatHighlightColor;
            string rgbaHighlight = FormattableString.Invariant($"rgba({highlightColor.R},{highlightColor.G},{highlightColor.B},{highlightColor.A / 255f:0.00})");

            Color modsColor = App.Settings.GeneralSettings.ChatHighlightModsColor;
            string rgbaMods = FormattableString.Invariant($"rgba({modsColor.R},{modsColor.G},{modsColor.B},{modsColor.A / 255f:0.00})");

            Color vipsColor = App.Settings.GeneralSettings.ChatHighlightVIPsColor;
            string rgbaVIPs = FormattableString.Invariant($"rgba({vipsColor.R},{vipsColor.G},{vipsColor.B},{vipsColor.A / 255f:0.00})");

            // Use a raw string literal to build the final CSS string
            string finalCss = $$"""
        /* Lines of the same message a bit closer, more room between messages */
        .chat_line {
            line-height: {{LineHeightPx}}px;
            padding: {{MessageGapPx}}px 0 !important;
        }

        /* Emotes and emojis stand on the bottom of the text, like the letters (the badges and the name are
           centered, see .badges img). The negative top margin keeps a tall emote from making its line taller. */
        .emoticon {
            max-height: 28px;
            margin: -4px -2px 0 !important;
            vertical-align: text-bottom !important;
        }

        .chat_line .message img.emoji {
            vertical-align: text-bottom !important;
        }

        /* Ensure badges are also aligned correctly. */
        .badges img {
            width: 20px;
            height: 20px;
            vertical-align: middle;
            margin-bottom: 2px; /* centered on the name's letters */
        }

        /* Ensure username and message flow as a single text block. */
        .username, .message {
            display: inline !important;
            vertical-align: middle;
        }

        /* User-defined highlight colors */
        .highlight { background-color: {{rgbaHighlight}} !important; }
        .highlightMod { background-color: {{rgbaMods}} !important; }
        .highlightVIP { background-color: {{rgbaVIPs}} !important; }
    """;

            return finalCss + GetMessageTextCss();
        }

        // Text options from the Chat settings page. The theme rules of browser/chat.css are more specific,
        // so every rule needs !important to win.
        private static string GetMessageTextCss()
        {
            var settings = App.Settings.GeneralSettings;
            var css = new System.Text.StringBuilder();
            const string lines = "#chat_box .chat_line, #chat_box .chat_line .nick, #chat_box .chat_line .message";

            // Only the message text: user names keep their Twitch color, and /me messages
            // (colored inline by browser/chat.js) keep the user's color too.
            if (System.Text.RegularExpressions.Regex.IsMatch(settings.ChatMessageColor ?? "", "^#[0-9A-Fa-f]{6}$"))
                css.Append($"\n#chat_box .chat_line .message:not([style*=\"color\"]) {{ color: {settings.ChatMessageColor} !important; }}");

            if (settings.ChatTextOutline == "soft")
                css.Append($"\n#chat_box, {lines} {{ text-shadow: 0 1px 3px rgba(0,0,0,.95), 0 0 2px rgba(0,0,0,.8) !important; }}");
            else if (settings.ChatTextOutline == "none")
                css.Append($"\n#chat_box, {lines} {{ text-shadow: none !important; }}");

            if (settings.ChatFontFamily is "Segoe UI" or "Arial" or "Verdana")
                css.Append($"\n#chat_box, {lines} {{ font-family: '{settings.ChatFontFamily}', sans-serif !important; letter-spacing: normal !important; }}");

            // The page writes the time on every line and hides it (browser/chat.css)
            if (settings.ShowMessageTime)
                css.Append($"\n#chat_box .chat_line .time_stamp {{ {MessageTimeCss} }}");

            return css.ToString();
        }
    }
}
