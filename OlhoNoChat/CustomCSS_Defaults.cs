namespace OlhoNoChat;

public static class CustomCSS_Defaults
{
    // Hides Twitch's own typing box when the app's "Escrever no chat…" box is used instead
    public const string HideTwitchChatBox = ".chat-input { display: none !important; }";

    // Twitch's own typing box (option of the Twitch tab) only shows while it is open: "Escrever" or the hotkey
    // put the class on the page while writing (see MainWindow.ChatInput.cs)
    public const string TwitchChatBoxWhileOpen = "body:not(.onc-writing) .chat-input { display: none !important; }";

    public static string TwitchPopoutChat = @"body { background-color: rgba(0,0,0,0) !important; }
body, p, span, div, a, h1, h2, h3 {
    text-shadow:
        -1px -1px 0 rgba(0, 0, 0, 0.85),
        1px -1px 0 rgba(0, 0, 0, 0.85),
        -1px  1px 0 rgba(0, 0, 0, 0.85),
        1px  1px 0 rgba(0, 0, 0, 0.85);
}
.stream-chat .stream-chat-header { display:none; background-color: rgba(0,0,0,0) !important; color: white !important; }
.chat-room__notifcations { display:none; }
.tw-z-default { display:none; }
.tw-flex { background-color: rgba(0,0,0,0) !important; }
.tw-root { background-color: rgba(0,0,0,0) !important; }
.tw-root--theme-dark { background-color: rgba(0,0,0,0) !important; }
.stream-chat { background-color: rgba(0,0,0,0) !important; }
.chat-room { background-color: rgba(0,0,0,0) !important; }
.chat-list { background-color: rgba(0,0,0,0) !important; }
.scrollable-area { background-color: rgba(0,0,0,0) !important; color: white !important; }
.chat-line__timestamp { color: gray !important; }
#chat-room-header-label { color: #cacaca !important; }

.chat-input-tray__open { background-color: rgba(0,0,0,0) !important; color: white !important; }
.chat-input-container__open { background-color: rgba(0,0,0,0) !important; color: white !important; }
.chat-wysiwyg-input__box { background-color: rgba(0,0,0,0) !important; color: white !important; }
.chat-wysiwyg-input__placeholder { color: #a9a9a9 !important; }
.font-scale--default:has([data-a-target=""chat-input""]) {
  background-color: rgba(0, 0, 0, 0.25) !important;
}
.community-highlight { background-color: rgba(0,0,0,0.75) !important; }
.marquee-animation { display: none !important; }

div:has(> div > div[aria-label=""Expand Top Gifters Leaderboard""]) {
  display: none !important;
}

.announcement-line { background-color: rgba(0,0,0,0.2) !important; }
div:has(> div.tw-svg + div) {
    background-color: transparent !important;
}

.tw-root--theme-light {
  background-color: rgba(0,0,0,0) !important;
  color: white !important;
}
.tw-root--theme-light svg { fill: white !important; }
.tw-root--theme-light input,
.tw-root--theme-light textarea { color: white !important; }
.tw-root--theme-light input::placeholder,
.tw-root--theme-light textarea::placeholder { color: #cacaca !important; }
.tw-root--theme-light [class*=""tw-border-""] { border-color: rgba(255, 255, 255, 0.25) !important; }

/* Buttons, notices and the gift leaderboard have their own background: the outline only makes their small letters heavy */
button, button *, [role=""button""], [role=""button""] *,
.tw-callout-message, .tw-callout-message *, [class*=""ScCallout""] *,
[class*=""channelLeaderboard""] *, .community-highlight * { text-shadow: none !important; }
";

    public static string NoneTheme_CustomCSS = @"#chat_box {
 text-shadow: 2px 2px 0 #000, 2px 2px 4px #000;
 letter-spacing: 1px;
}

.chat_line {
 color: #fff;
 font-size: 16px!important;
 font-weight: bold;
}

.chat_line .nick {

}

.message { display: inline !important; }
.highlight { background-color: rgba(255,255,0,0.5) !important; }";
}
