using System.Text;

namespace OlhoNoChat.Chats
{
    public class CustomURLChat : Chat
    {
        public CustomURLChat(ChatTypes chatType) : base(chatType)
        {
        }

        public override string SetupJavascript()
        {
            if (this.ChatType != ChatTypes.TwitchPopout)
                return string.Empty;

            // Writing in Twitch's own box after the hotkey: Enter (message sent, box emptied) or Esc
            // tells the app to give the focus back to the game (see MainWindow.ChatInput.cs).
            // Every new message gets the time it arrived (shown only with "Mostrar o horário das mensagens").
            return """
                (function () {
                    if (!window.oncTimeHooked) {
                        window.oncTimeHooked = true;
                        const stamp = (line) => {
                            if (line.querySelector('.onc-time')) return;
                            const time = document.createElement('span');
                            time.className = 'onc-time';
                            time.textContent = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
                            const before = line.querySelector('.chat-line__message--badges, .chat-line__username-container, .chat-line__username');
                            if (before) before.parentNode.insertBefore(time, before); else line.prepend(time);
                        };
                        new MutationObserver((changes) => changes.forEach(change => change.addedNodes.forEach(node => {
                            if (node.nodeType !== 1) return;
                            if (node.matches('.chat-line__message')) stamp(node);
                            else node.querySelectorAll && node.querySelectorAll('.chat-line__message').forEach(stamp);
                        }))).observe(document.body, { childList: true, subtree: true });
                    }

                    if (window.oncComposeHooked) return;
                    window.oncComposeHooked = true;
                    document.addEventListener('keydown', function (e) {
                        var input = e.target && e.target.closest && e.target.closest('[data-a-target="chat-input"]');
                        if (!input || !window.chrome || !window.chrome.webview) return;
                        if (e.key === 'Escape') {
                            window.chrome.webview.postMessage('onc:compose-cancel');
                        } else if (e.key === 'Enter' && !e.shiftKey) {
                            setTimeout(function () {
                                if (!input.innerText.trim()) window.chrome.webview.postMessage('onc:compose-sent');
                            }, 250);
                        }
                    }, true);
                })();
                """;
        }

        public override string SetupCustomCSS()
        {
            string css = string.Empty;

            if (this.ChatType == ChatTypes.TwitchPopout)
            {
                if (App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS)
                    css = CustomCSS_Defaults.TwitchPopoutChat;
                else if (!string.IsNullOrEmpty(App.Settings.GeneralSettings.TwitchPopoutCSS))
                    css = App.Settings.GeneralSettings.TwitchPopoutCSS;

                // Messages with the look of the "Padrão" chat (with the app's own look only)
                if (App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS)
                    css += "\n" + MessageLookCss();

                // Only one typing box at a time, and Twitch's own only while it is open
                css += "\n" + (App.Settings.GeneralSettings.UseTwitchChatBox
                    ? CustomCSS_Defaults.TwitchChatBoxWhileOpen
                    : CustomCSS_Defaults.HideTwitchChatBox);

                // Our time (SetupJavascript); Twitch's own one would show a second time
                css += App.Settings.GeneralSettings.ShowMessageTime
                    ? $"\n.chat-line__message .onc-time {{ {PadraoChat.MessageTimeCss} }}\n.chat-line__timestamp {{ display: none !important; }}"
                    : "\n.onc-time { display: none !important; }";
            }
            else
            {
                if (!string.IsNullOrEmpty(App.Settings.GeneralSettings.CustomCSS))
                    css = App.Settings.GeneralSettings.CustomCSS;
            }

            return css;
        }

        // The "Chat oficial da Twitch" follows the "Padrão" chat: same font, size and spacing (PadraoChat's
        // constants) and the "Texto das mensagens" options of the Aparência tab. With the FrankerFaceZ
        // extension or without it, each message is a ".chat-line__message".
        private static string MessageLookCss()
        {
            var settings = App.Settings.GeneralSettings;
            // The theme's font has 1 px between letters; a chosen font keeps its own spacing (as in the "Padrão" chat)
            bool chosenFont = settings.ChatFontFamily is "Segoe UI" or "Arial" or "Verdana";
            string font = chosenFont ? $"'{settings.ChatFontFamily}', sans-serif" : PadraoChat.MessageFontFamily;
            string letterSpacing = chosenFont ? "normal" : "1px";

            var css = new StringBuilder($$"""
                .chat-line__message {
                    font-family: {{font}} !important;
                    font-size: {{PadraoChat.MessageFontSizePx}}px !important;
                    font-weight: 700 !important;
                    letter-spacing: {{letterSpacing}} !important;
                    line-height: {{PadraoChat.LineHeightPx}}px !important;
                    padding-top: {{PadraoChat.MessageGapPx}}px !important;
                    padding-bottom: {{PadraoChat.MessageGapPx}}px !important;
                }
                .chat-line__message .text-fragment, .chat-line__message .mention-fragment, .chat-line__message .message,
                .chat-line__message .chat-author__display-name, .chat-line__message [data-a-target="chat-line-message-body"] {
                    font: inherit !important;
                }
                .chat-line__message .chat-image, .chat-line__message .ffz-emote, .chat-line__message .emoji, .chat-line__message .ffz-emoji {
                    margin-top: -4px !important;
                    margin-bottom: 0 !important;
                    vertical-align: text-bottom !important;
                }
                """);

            // Only the message text: the names keep their Twitch color
            if (System.Text.RegularExpressions.Regex.IsMatch(settings.ChatMessageColor ?? "", "^#[0-9A-Fa-f]{6}$"))
                css.Append($"\n.chat-line__message .text-fragment {{ color: {settings.ChatMessageColor} !important; }}");

            // "Contorno preto" is the outline of the default CSS above
            const string lines = ".chat-line__message, .chat-line__message *";
            if (settings.ChatTextOutline == "soft")
                css.Append($"\n{lines} {{ text-shadow: 0 1px 3px rgba(0,0,0,.95), 0 0 2px rgba(0,0,0,.8) !important; }}");
            else if (settings.ChatTextOutline == "none")
                css.Append($"\n{lines} {{ text-shadow: none !important; }}");

            return css.ToString();
        }
    }
}
