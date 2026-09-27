using System.Text.Json;
using System.Web;
using Color = System.Windows.Media.Color;

namespace OlhoNoChat.Chats
{
    public class KapChat : Chat
    {
        public KapChat() : base(ChatTypes.KapChat)
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

        // "theme=" of the KapChat address; the index is GeneralSettings.ThemeIndex: 0 "Nenhum" (no theme), 1 "Padrão"
        public static readonly string[] Themes = { string.Empty, "bttv_blackchat" };

        // A line in the user's color, like a /me message (channel point redemptions)
        public override string PushNewChatMessage(string message, string nick, string color)
        {
            string safeNickForJs = string.IsNullOrEmpty(nick) ? "null" : JsonSerializer.Serialize(nick);
            string safeColorForJs = JsonSerializer.Serialize(color);

            // Only the message is escaped; the \x01 characters and the quotes around it are added here
            string escapedMessage = HttpUtility.JavaScriptStringEncode(message);
            string finalActionArgument = $"\"\\x01ACTION {escapedMessage}\\x01\"";

            return $"var ttags = {{ color: {safeColorForJs} }};\n" +
                   $"Chat.insert({safeNickForJs}, ttags, {finalActionArgument});";
        }

        // Filters and sound switch read by the page on every message. The page keeps them in
        // window.oncChatSettings, so saving the settings can update them without reloading the chat.
        public static string GetMessageSettingsJson()
        {
            var settings = App.Settings.GeneralSettings;

            var vipList = settings.AllowedUsersList?.Cast<string>().Select(u => u.ToLowerInvariant()).ToList() ?? new List<string>();
            var blockList = settings.BlockedUsersList?.Cast<string>().Select(u => u.ToLowerInvariant()).ToList() ?? new List<string>();

            return JsonSerializer.Serialize(new
            {
                highlightUsers = settings.HighlightUsersChat,
                allowedUsersOnly = settings.AllowedUsersOnlyChat,
                playSound = settings.ChatNotificationSound?.ToLower() != "none",
                filterAllowAllVIPs = settings.FilterAllowAllVIPs,
                filterAllowAllMods = settings.FilterAllowAllMods,
                vips = vipList,
                blockList = blockList
            });
        }

        public override string SetupJavascript()
        {
            string messageSettingsJson = GetMessageSettingsJson();

            string username = App.Settings.GeneralSettings.Username;

            // Shown in the connection status ("Conectando ao chat de ...")
            string channelName = Twitch.TwitchNames.Extract(username);
            string channelNameJson = JsonSerializer.Serialize(channelName.Length > 0 ? channelName : username ?? string.Empty);

            string finalScript = $$"""
(function() {
    'use strict';

    const MAX_RETRIES = 20;
    let currentRetry = 0;
    const SCRIPT_ID = 'KapChat_Wrapper_v2.3_Robust';

    function logToWebViewConsole(level, message) {
        console.log(`[${SCRIPT_ID} - ${level.toUpperCase()}]: ${message}`);
    }

    logToWebViewConsole('info', 'KapChat wrapper injection script started.');

    // 7TV emotes, the channel's and the global ones. KapChat itself already shows the BTTV and FFZ
    // emotes; the wrapper below turns the 7TV ones into pictures. Loaded once per page.
    const sevenTv = window.oncSevenTv || (window.oncSevenTv = loadSevenTvEmotes());

    function loadSevenTvEmotes() {
        const emotes = { channel: new Map(), global: new Map() };
        function load(url, target, emoteList) {
            fetch(url)
                .then(response => response.ok ? response.json() : null) // 404: the channel doesn't use 7TV
                .then(data => {
                    (emoteList(data) || []).forEach(emote => target.set(emote.name, (emote.data && emote.data.id) || emote.id));
                    logToWebViewConsole('info', `7TV: ${target.size} emotes from ${url}`);
                })
                .catch(e => logToWebViewConsole('warn', `Could not load the 7TV emotes from ${url}: ${e.message}`));
        }

        load('https://7tv.io/v3/emote-sets/global', emotes.global, data => data && data.emotes);

        // KapChat gets the channel id before it connects to the chat, so no message arrives before it
        (function waitForChannelId(tries) {
            const id = typeof Chat !== 'undefined' && Chat.vars && Chat.vars.channelId;
            if (id) load('https://7tv.io/v3/users/twitch/' + encodeURIComponent(id), emotes.channel, data => data && data.emote_set && data.emote_set.emotes);
            else if (tries < 240) setTimeout(() => waitForChannelId(tries + 1), 250);
        })(0);

        return emotes;
    }

    // The words of a message that are 7TV emotes become pictures (a channel emote wins over a global one)
    function addSevenTvEmotes(html) {
        return html.split(' ').map(word => {
            const id = sevenTv.channel.get(word) || sevenTv.global.get(word);
            if (!id) return word;
            const name = word.replace(/"/g, '&quot;');
            return `<img class="emote" src="https://cdn.7tv.app/emote/${id}/1x.webp" alt="${name}" title="${name}">`;
        }).join(' ');
    }

    function performChatInsertModification() {
        if (typeof Chat === 'undefined' || typeof Chat.insert !== 'function') {
            currentRetry++;
            if (currentRetry < MAX_RETRIES) {
                setTimeout(performChatInsertModification, 500);
            } else {
                logToWebViewConsole('error', 'Failed to find Chat.insert after max retries.');
            }
            return;
        }

        if (Chat.insert.isWrappedByMyScript) {
            return;
        }

        const CSHARP_SETTINGS = {{messageSettingsJson}};
        window.oncChatSettings = CSHARP_SETTINGS;

        setupConnectionStatus();

        const originalChatInsert = Chat.insert;
        Chat.insert = function(nick, tags, message) {
            if (!nick && typeof message === 'string') {
                const state = connectionState(message);
                if (state) {
                    window.oncSetChatState(state);
                    return;
                }
            }

            const lowerNick = (nick || '').toLowerCase();
            if (CSHARP_SETTINGS.blockList.includes(lowerNick)) return;

            // "VIPs" and "Mods" filters, from the badges tag ("vip/1,subscriber/12"). Mod wins over VIP.
            let allowOtherBasedOnTags = false;
            let highlightSuffix = '';
            if ((CSHARP_SETTINGS.filterAllowAllVIPs || CSHARP_SETTINGS.filterAllowAllMods) && tags && typeof tags.badges === 'string') {
                if (CSHARP_SETTINGS.filterAllowAllVIPs && /(?:^|,)vip(?:\/|,|$)/.test(tags.badges)) {
                    highlightSuffix = 'VIP';
                    allowOtherBasedOnTags = true;
                }
                if (CSHARP_SETTINGS.filterAllowAllMods && /(?:^|,)moderator(?:\/|,|$)/.test(tags.badges)) {
                    highlightSuffix = 'Mod';
                    allowOtherBasedOnTags = true;
                }
            }

            const isListed = CSHARP_SETTINGS.vips.includes(lowerNick);
            if (CSHARP_SETTINGS.allowedUsersOnly && !isListed && !allowOtherBasedOnTags && nick) return;

            const shouldHighlight = CSHARP_SETTINGS.highlightUsers && (isListed || allowOtherBasedOnTags);

            if (CSHARP_SETTINGS.playSound) {
                const shouldPlaySound = !CSHARP_SETTINGS.highlightUsers && !CSHARP_SETTINGS.allowedUsersOnly ||
                                        (CSHARP_SETTINGS.highlightUsers && shouldHighlight) ||
                                        (CSHARP_SETTINGS.allowedUsersOnly && (isListed || allowOtherBasedOnTags));
                // The app decides if it rings (not while the sound is playing, "Quando tocar")
                if (shouldPlaySound && window.chrome && window.chrome.webview) window.chrome.webview.postMessage('onc:play-sound');
            }

            const originalQueuePush = Chat.vars.queue.push;
            let capturedHtml = '';
            Chat.vars.queue.push = (html) => { capturedHtml += html; };

            try {
                originalChatInsert.apply(this, arguments);
            } finally {
                Chat.vars.queue.push = originalQueuePush;
            }

            if (capturedHtml) {
                const tempDiv = document.createElement('div');
                tempDiv.innerHTML = capturedHtml;
                const chatLine = tempDiv.querySelector('.chat_line');
                const messageSpan = tempDiv.querySelector('.chat_line .message'); 

                if (messageSpan) {
                    try {
                        messageSpan.innerHTML = addSevenTvEmotes(messageSpan.innerHTML);
                    } catch (e) {
                        logToWebViewConsole('error', `Failed to add the 7TV emotes: ${e.message}`);
                    }
                    if (nick) linkify(messageSpan);
                }

                if (chatLine && shouldHighlight) {
                    chatLine.classList.add(`highlight${highlightSuffix}`);
                }

                // Push the fully modified HTML
                Chat.vars.queue.push.call(Chat.vars.queue, tempDiv.innerHTML);

            } else {
                 // Fallback if HTML capture fails
                originalChatInsert.apply(this, arguments);
            }
        };
        Chat.insert.isWrappedByMyScript = true;
        logToWebViewConsole('info', 'SUCCESS: Chat.insert fully modified for emotes and highlighting.');
        setupScrolling();
    }

    // Web addresses in the messages become links (opened in the user's browser by the app, and only
    // while the borders are visible: the app sets "onc-links-on", see MainWindow.Links.cs)
    const LINK = /\b(?:https?:\/\/|www\.)[^\s<>"]+[^\s<>".,:;!?)\]'}]/gi;
    function linkify(root) {
        // Quick check on the whole text first (no \b here: an emoji picture between two words joins their text)
        if (!/https?:\/\/|www\./i.test(root.textContent)) return;
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const texts = [];
        while (walker.nextNode()) texts.push(walker.currentNode);
        texts.forEach(node => {
            const text = node.nodeValue;
            LINK.lastIndex = 0;
            if (!LINK.test(text)) return;
            LINK.lastIndex = 0;
            const parts = document.createDocumentFragment();
            let last = 0, match;
            while ((match = LINK.exec(text))) {
                parts.appendChild(document.createTextNode(text.slice(last, match.index)));
                const a = document.createElement('a');
                a.className = 'onc-link';
                a.href = /^www\./i.test(match[0]) ? 'https://' + match[0] : match[0];
                a.target = '_blank';
                a.rel = 'noopener noreferrer';
                a.textContent = match[0];
                parts.appendChild(a);
                last = match.index + match[0].length;
            }
            parts.appendChild(document.createTextNode(text.slice(last)));
            node.parentNode.replaceChild(parts, node);
        });
    }

    // KapChat's connection lines ("Connecting to chat server..", "Connected.", "Joined channel: x.",
    // "You were disconnected from the server.") and the state each one means
    function connectionState(text) {
        if (text === 'Connecting to chat server..' || text === 'Connected.') return 'connecting';
        if (text === 'You were disconnected from the server.') return 'disconnected';
        if (/^Joined channel: /.test(text)) return 'connected';
        return null;
    }

    // Instead of those lines, a dot at the top: yellow and pulsing while connecting, green when
    // connected (shown for a moment, then it fades out), red while the connection is lost. The app
    // shows the same dot next to the channel name (onc:chat-state, see MainWindow.ChannelBar.cs).
    function setupConnectionStatus() {
        if (window.oncSetChatState) return;

        const style = document.createElement('style');
        style.textContent =
            '#onc-status { position: fixed; top: 6px; left: 8px; z-index: 30; display: flex; align-items: center; gap: 8px;' +
            '  background: rgba(20,20,20,.88); color: #fff; border-radius: 14px; padding: 5px 12px 5px 10px;' +
            '  font: 600 15px "Segoe UI", Arial, sans-serif; letter-spacing: 0; text-shadow: none; white-space: nowrap;' +
            '  pointer-events: none; transition: opacity .2s; }' +
            '#onc-status.faded { opacity: 0; transition: opacity .8s; }' +
            '.onc-link { color: #8FC8FF !important; text-decoration: underline; pointer-events: none; }' +
            'body.onc-links-on .onc-link { pointer-events: auto; cursor: pointer; }' +
            '#onc-status .onc-dot { width: 10px; height: 10px; border-radius: 50%; flex: none; }' +
            '#onc-status.connecting .onc-dot { background: #F5C518; animation: onc-pulse 1s ease-in-out infinite; }' +
            '#onc-status.connected .onc-dot { background: #3BD16F; }' +
            '#onc-status.disconnected .onc-dot { background: #F04A4A; }' +
            '@keyframes onc-pulse { 0%, 100% { opacity: 1; transform: scale(1); } 50% { opacity: .3; transform: scale(.7); } }';
        document.head.appendChild(style);

        const pill = document.createElement('div');
        pill.id = 'onc-status';
        const dot = document.createElement('span');
        dot.className = 'onc-dot';
        const text = document.createElement('span');
        pill.appendChild(dot);
        pill.appendChild(text);
        document.body.appendChild(pill);

        const channel = {{channelNameJson}};
        const texts = {
            connecting: 'Conectando ao chat de ' + channel + '…',
            connected: 'Conectado ao chat de ' + channel,
            disconnected: 'Sem conexão com o chat. Tentando de novo…'
        };
        let fadeTimer = null;
        window.oncSetChatState = function (state) {
            if (!texts[state]) return;
            pill.className = state;
            text.textContent = texts[state];
            clearTimeout(fadeTimer);
            if (state === 'connected') fadeTimer = setTimeout(() => pill.classList.add('faded'), 4000);
            try {
                if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('onc:chat-state:' + state);
            } catch (e) { /* not in the app */ }
        };

        // Lines KapChat added before this script was ready: taken out, the last state kept
        let last = 'connecting';
        const isStatusHtml = (html) => html.indexOf('data-nick="Chat"') >= 0;
        Chat.vars.queue = Chat.vars.queue.filter(html => {
            if (!isStatusHtml(html)) return true;
            const holder = document.createElement('div');
            holder.innerHTML = html;
            const message = holder.querySelector('.message');
            const state = message && connectionState(message.textContent.trim());
            if (state) last = state;
            return !state;
        });
        document.querySelectorAll('#chat_box .chat_line[data-nick="Chat"]').forEach(line => {
            const message = line.querySelector('.message');
            const state = message && connectionState(message.textContent.trim());
            if (state) {
                last = state;
                line.remove();
            }
        });
        window.oncSetChatState(last);
    }

    // Keeps the newest message fully visible and lets the chat be scrolled back while the
    // window accepts clicks ("modo rolagem"). The host calls window.oncSetScrollMode().
    function setupScrolling() {
        const box = document.getElementById('chat_box');
        if (!box || box.oncScrollReady) return;
        box.oncScrollReady = true;

        const native = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop');
        const isAtBottom = () => box.scrollHeight - native.get.call(box) - box.clientHeight < 4;
        const scrollToBottom = () => native.set.call(box, box.scrollHeight);
        let pinned = true;

        // KapChat forces the scroll to the bottom on every new message; only allow it while pinned.
        Object.defineProperty(box, 'scrollTop', {
            get() { return native.get.call(this); },
            set(value) { if (pinned) native.set.call(this, value); },
            configurable: true
        });

        const style = document.createElement('style');
        style.textContent =
            '#chat_box::-webkit-scrollbar { width: 6px; }' +
            '#chat_box::-webkit-scrollbar-track { background: transparent; }' +
            '#chat_box::-webkit-scrollbar-thumb { background: rgba(255,255,255,.35); border-radius: 3px; }' +
            '.onc-pill { position: fixed; left: 50%; transform: translateX(-50%); z-index: 20; display: none;' +
            '  background: #141414; color: #fff; border: 2px solid #FF8A65; border-radius: 16px; padding: 6px 14px;' +
            '  font: bold 17px "Segoe UI", Arial, sans-serif; letter-spacing: 0; text-shadow: none; white-space: nowrap; }' +
            '#onc-scroll-banner { top: 6px; left: 8px; right: 8px; transform: none; white-space: pre-line; text-align: center; border-radius: 8px; }' +
            '#onc-new-messages { bottom: 10px; cursor: pointer; }';
        document.head.appendChild(style);

        const banner = document.createElement('div');
        banner.id = 'onc-scroll-banner';
        banner.className = 'onc-pill';
        banner.style.cursor = 'pointer';
        banner.addEventListener('click', () => {
            if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('onc:exit-scroll-mode');
        });
        const newMessages = document.createElement('div');
        newMessages.id = 'onc-new-messages';
        newMessages.className = 'onc-pill';
        newMessages.textContent = '↓ Novas mensagens';
        document.body.appendChild(banner);
        document.body.appendChild(newMessages);

        function pin() {
            pinned = true;
            Chat.vars.max_messages = 100;
            newMessages.style.display = 'none';
            scrollToBottom();
        }
        newMessages.addEventListener('click', pin);

        box.addEventListener('scroll', () => {
            pinned = isAtBottom();
            // Don't delete old lines while they are being read.
            Chat.vars.max_messages = pinned ? 100 : 1000;
            if (pinned) newMessages.style.display = 'none';
        });

        // Emotes and badges load after their line is added and make it taller.
        box.addEventListener('load', () => { if (pinned) scrollToBottom(); }, true);
        window.addEventListener('resize', () => { if (pinned) scrollToBottom(); });
        new MutationObserver(() => {
            if (pinned) scrollToBottom();
            else newMessages.style.display = 'block';
        }).observe(box, { childList: true });

        window.oncSetScrollMode = function (mode) {
            box.style.overflowY = mode.enabled ? 'auto' : 'hidden';
            banner.textContent = 'Modo rolagem: use a rodinha do mouse\n' + (mode.hotkey ? 'Clique aqui ou aperte ' + mode.hotkey + ' para sair' : 'Clique aqui para sair');
            banner.style.display = mode.enabled && mode.banner ? 'block' : 'none';
            if (!mode.enabled) pin();
        };
        if (window.oncScrollModeWanted) window.oncSetScrollMode(window.oncScrollModeWanted);
    }

    performChatInsertModification();
})();
""";

            return finalScript;
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
        .emote, .emoticon {
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

        // Text options from the Chat settings page. The theme stylesheet is loaded by the page after
        // this CSS is injected, so every rule needs !important to win.
        private static string GetMessageTextCss()
        {
            var settings = App.Settings.GeneralSettings;
            var css = new System.Text.StringBuilder();
            const string lines = "#chat_box .chat_line, #chat_box .chat_line .nick, #chat_box .chat_line .message";

            // Only the message text: user names keep their Twitch color, and /me messages
            // (colored inline by KapChat) keep the user's color too.
            if (System.Text.RegularExpressions.Regex.IsMatch(settings.ChatMessageColor ?? "", "^#[0-9A-Fa-f]{6}$"))
                css.Append($"\n#chat_box .chat_line .message:not([style*=\"color\"]) {{ color: {settings.ChatMessageColor} !important; }}");

            if (settings.ChatTextOutline == "soft")
                css.Append($"\n#chat_box, {lines} {{ text-shadow: 0 1px 3px rgba(0,0,0,.95), 0 0 2px rgba(0,0,0,.8) !important; }}");
            else if (settings.ChatTextOutline == "none")
                css.Append($"\n#chat_box, {lines} {{ text-shadow: none !important; }}");

            if (settings.ChatFontFamily is "Segoe UI" or "Arial" or "Verdana")
                css.Append($"\n#chat_box, {lines} {{ font-family: '{settings.ChatFontFamily}', sans-serif !important; letter-spacing: normal !important; }}");

            // KapChat writes the time on every line and hides it
            if (settings.ShowMessageTime)
                css.Append($"\n#chat_box .chat_line .time_stamp {{ {MessageTimeCss} }}");

            return css.ToString();
        }
    }
}
