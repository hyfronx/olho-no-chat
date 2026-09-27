// The "Padrão" chat: reads the chat of a Twitch channel (anonymous IRC, read-only) and shows its messages.
// Opened by the app as chat.html?canal=<name>&tema=padrao; it connects when the app calls
// window.oncChat.start(settings), so the settings already apply to the first message.
(function () {
    'use strict';

    const IRC_URL = 'wss://irc-ws.chat.twitch.tv:443';
    const KEEP_ALIVE_MS = 30000;      // a PING when nothing came for this long...
    const SILENCE_LIMIT_MS = 75000;   // ...and a new connection when even the PONG doesn't come
    const RETRY_MIN_MS = 1000;
    const RETRY_MAX_MS = 30000;
    const MAX_LINES = 100;

    const params = new URLSearchParams(location.search);
    const channel = (params.get('canal') || '').toLowerCase();
    const darkTheme = params.get('tema') === 'padrao';
    if (darkTheme)
        document.body.classList.add('tema-padrao');

    const box = document.getElementById('chat_box');

    let settings = { fade: 0, hideBots: true };
    let started = false;
    let socket = null;
    let lastData = 0;
    let retryDelay = RETRY_MIN_MS;
    let retryTimer = null;

    function postToApp(message) {
        try {
            if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
        } catch (e) { /* not in the app */ }
    }

    // --- Connection ----------------------------------------------------------------------------------

    function connect() {
        clearTimeout(retryTimer);
        setState('connecting');

        const ws = new WebSocket(IRC_URL);
        socket = ws;
        lastData = Date.now();

        ws.onopen = () => {
            ws.send('CAP REQ :twitch.tv/tags twitch.tv/commands');
            ws.send('PASS SCHMOOPIIE');
            ws.send('NICK justinfan' + Math.floor(10000 + Math.random() * 80000));
            ws.send('JOIN #' + channel);
        };
        ws.onmessage = (event) => {
            if (ws !== socket) return;
            lastData = Date.now();
            String(event.data).split('\r\n').forEach(line => {
                if (!line) return;
                try {
                    handleLine(parseIrc(line));
                } catch (e) {
                    console.error('[ONC] Could not handle the line: ' + line, e);
                }
            });
        };
        ws.onclose = () => {
            if (ws !== socket) return;
            socket = null;
            if (state !== 'unavailable') setState('disconnected');
            retryTimer = setTimeout(connect, retryDelay);
            retryDelay = Math.min(retryDelay * 2, RETRY_MAX_MS);
        };
    }

    // Twitch sends a PING every few minutes; in between, ours keep a healthy connection talking.
    // A connection that stopped answering is replaced (onclose connects again).
    setInterval(() => {
        if (!socket || socket.readyState !== WebSocket.OPEN) return;
        const silentFor = Date.now() - lastData;
        if (silentFor > SILENCE_LIMIT_MS) socket.close();
        else if (silentFor > KEEP_ALIVE_MS) socket.send('PING :olhonochat');
    }, 5000);

    // One IRC line: "@tags :prefix COMMAND params :trailing"
    function parseIrc(line) {
        const message = { tags: {}, prefix: '', command: '', params: [] };
        let rest = line;

        if (rest[0] === '@') {
            const end = rest.indexOf(' ');
            rest.slice(1, end).split(';').forEach(pair => {
                const eq = pair.indexOf('=');
                const key = eq < 0 ? pair : pair.slice(0, eq);
                message.tags[key] = eq < 0 ? '' : unescapeTag(pair.slice(eq + 1));
            });
            rest = rest.slice(end + 1);
        }
        if (rest[0] === ':') {
            const end = rest.indexOf(' ');
            message.prefix = rest.slice(1, end);
            rest = rest.slice(end + 1);
        }

        const trailing = rest.indexOf(' :');
        const head = trailing < 0 ? rest : rest.slice(0, trailing);
        const words = head.split(' ').filter(Boolean);
        message.command = words.shift() || '';
        message.params = words;
        if (trailing >= 0) message.params.push(rest.slice(trailing + 2));
        return message;
    }

    function unescapeTag(value) {
        return value.replace(/\\(.?)/g, (_, c) => ({ s: ' ', ':': ';', '\\': '\\', r: '\r', n: '\n' })[c] ?? c);
    }

    function handleLine(message) {
        switch (message.command) {
            case 'PING':
                socket.send('PONG :' + (message.params[0] || 'tmi.twitch.tv'));
                break;
            case 'ROOMSTATE':
                // Comes right after joining the channel (and again when a chat mode changes)
                retryDelay = RETRY_MIN_MS;
                setState('connected');
                break;
            case 'PRIVMSG':
                onChatMessage(message);
                break;
            case 'CLEARCHAT':
                // A timeout or ban takes that user's messages out; without a user, /clear empties the chat
                if (message.params[1]) removeLines(`.chat_line[data-nick="${CSS.escape(message.params[1].toLowerCase())}"]`);
                else box.replaceChildren();
                break;
            case 'CLEARMSG':
                if (message.tags['target-msg-id']) removeLines(`.chat_line[data-id="${CSS.escape(message.tags['target-msg-id'])}"]`);
                break;
            case 'NOTICE':
                if (message.tags['msg-id'] === 'msg_channel_suspended') setState('unavailable');
                break;
            case 'RECONNECT':
                // Twitch is about to restart this server: onclose connects again at once
                retryDelay = RETRY_MIN_MS;
                if (socket) socket.close();
                break;
        }
    }

    // --- Connection status ---------------------------------------------------------------------------
    // The app shows the same dot next to the channel name (onc:chat-state, see MainWindow.ChannelBar.cs)

    let state = null;
    const pill = document.createElement('div');
    pill.id = 'onc-status';
    const pillDot = document.createElement('span');
    pillDot.className = 'onc-dot';
    const pillText = document.createElement('span');
    pill.append(pillDot, pillText);
    document.body.appendChild(pill);
    let pillFadeTimer = null;

    const stateTexts = {
        connecting: 'Conectando ao chat de ' + channel + '…',
        connected: 'Conectado ao chat de ' + channel,
        disconnected: 'Sem conexão com o chat. Tentando de novo…',
        unavailable: 'O canal ' + channel + ' não existe ou está suspenso'
    };

    function setState(newState) {
        if (newState === state) return;
        state = newState;
        pill.className = newState;
        pillText.textContent = stateTexts[newState];
        clearTimeout(pillFadeTimer);
        if (newState === 'connected') pillFadeTimer = setTimeout(() => pill.classList.add('faded'), 4000);
        postToApp('onc:chat-state:' + (newState === 'unavailable' ? 'disconnected' : newState));
    }

    // --- Messages ------------------------------------------------------------------------------------

    function onChatMessage(message) {
        if (message.params[0] !== '#' + channel) return;
        const login = message.prefix.split('!')[0];
        let text = message.params[1] || '';

        if (settings.hideBots && (text[0] === '!' || /bot$/.test(login))) return;

        let action = false;
        const actionMatch = /^\x01ACTION (.*)\x01$/.exec(text);
        if (actionMatch) {
            action = true;
            text = actionMatch[1];
        }

        const tags = message.tags;
        addLine({
            id: tags.id,
            login,
            name: tags['display-name'] || login,
            color: userColor(login, tags.color),
            action,
            parts: splitTwitchEmotes(text, tags.emotes)
        });
    }

    // The "emotes" tag ("25:0-4,12-16/1902:6-10") gives each emote's place counted in characters
    // (code points, so an emoji before an emote counts as one)
    function splitTwitchEmotes(text, emotesTag) {
        if (!emotesTag) return [text];

        const places = [];
        emotesTag.split('/').forEach(entry => {
            const [id, ranges] = entry.split(':');
            if (!ranges) return;
            ranges.split(',').forEach(range => {
                const [first, last] = range.split('-').map(Number);
                places.push({ id, first, last });
            });
        });
        places.sort((a, b) => a.first - b.first);

        const chars = Array.from(text);
        const parts = [];
        let next = 0;
        places.forEach(place => {
            if (place.first < next || place.last >= chars.length) return;
            if (place.first > next) parts.push(chars.slice(next, place.first).join(''));
            const name = chars.slice(place.first, place.last + 1).join('');
            parts.push({ emote: twitchEmoteUrls(place.id), name });
            next = place.last + 1;
        });
        if (next < chars.length) parts.push(chars.slice(next).join(''));
        return parts;
    }

    function twitchEmoteUrls(id) {
        const base = 'https://static-cdn.jtvnw.net/emoticons/v2/' + encodeURIComponent(id) + '/default/dark/';
        return { src: base + '1.0', srcset: base + '2.0 2x, ' + base + '3.0 4x' };
    }

    // Users without a color get one of Twitch's default colors. On the dark theme, a color too dark to read
    // on the black background is made lighter.
    const DEFAULT_COLORS = ['#FF0000', '#0000FF', '#008000', '#B22222', '#FF7F50', '#9ACD32', '#FF4500', '#2E8B57',
                            '#DAA520', '#D2691E', '#5F9EA0', '#1E90FF', '#FF69B4', '#8A2BE2', '#00FF7F'];
    const colorCache = new Map();

    function userColor(login, color) {
        if (!/^#[0-9a-f]{6}$/i.test(color || '')) color = DEFAULT_COLORS[login.charCodeAt(0) % DEFAULT_COLORS.length];
        if (!darkTheme) return color;

        let readable = colorCache.get(color);
        if (!readable) {
            let [r, g, b] = [1, 3, 5].map(i => parseInt(color.substr(i, 2), 16));
            // YIQ brightness: 128 and above reads well on black. Each step mixes in some white.
            while ((r * 299 + g * 587 + b * 114) / 1000 < 128) {
                r = Math.round(r + (255 - r) * 0.15);
                g = Math.round(g + (255 - g) * 0.15);
                b = Math.round(b + (255 - b) * 0.15);
            }
            readable = '#' + [r, g, b].map(v => v.toString(16).padStart(2, '0')).join('');
            colorCache.set(color, readable);
        }
        return readable;
    }

    // One chat line: time, name, ":" and the message (a /me message has no ":" and is in the user's color)
    function addLine(line) {
        const div = document.createElement('div');
        div.className = 'chat_line';
        if (line.id) div.dataset.id = line.id;
        div.dataset.nick = line.login;
        div.dataset.timestamp = Date.now();
        div.style.setProperty('--user-color', line.color);

        const time = document.createElement('span');
        time.className = 'time_stamp';
        time.textContent = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });

        const nick = document.createElement('span');
        nick.className = 'nick';
        nick.style.color = line.color;
        nick.textContent = line.name;

        const message = document.createElement('span');
        message.className = 'message';
        if (line.action) message.style.color = line.color;
        line.parts.forEach(part => {
            if (typeof part === 'string') {
                message.append(part);
                return;
            }
            const img = document.createElement('img');
            img.className = 'emoticon';
            img.src = part.emote.src;
            if (part.emote.srcset) img.srcset = part.emote.srcset;
            img.alt = img.title = part.name;
            message.append(img);
        });

        div.append(time, nick);
        if (!line.action) {
            const colon = document.createElement('span');
            colon.className = 'colon';
            colon.textContent = ':';
            div.append(colon);
        }
        div.append(' ', message);

        box.appendChild(div);
        scheduleLayout();
    }

    function removeLines(selector) {
        box.querySelectorAll(selector).forEach(line => line.remove());
    }

    // Old lines are taken out and the newest one scrolled into view once per frame, even in a busy chat
    let layoutPending = false;
    function scheduleLayout() {
        if (layoutPending) return;
        layoutPending = true;
        requestAnimationFrame(() => {
            layoutPending = false;
            let extra = box.children.length - MAX_LINES;
            while (extra-- > 0) box.firstElementChild.remove();
            box.scrollTop = box.scrollHeight;
        });
    }

    // Emotes load after their line is added and make it taller
    box.addEventListener('load', () => { box.scrollTop = box.scrollHeight; }, true);
    window.addEventListener('resize', () => { box.scrollTop = box.scrollHeight; });

    // "Apagar mensagens antigas": a line older than settings.fade seconds fades out
    setInterval(() => {
        if (!settings.fade) return;
        const limit = Date.now() - settings.fade * 1000;
        for (const line of box.children) {
            if (Number(line.dataset.timestamp) > limit) break;
            if (line.classList.contains('on_out')) continue;
            line.classList.add('on_out');
            line.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 400 }).onfinish = () => line.remove();
        }
    }, 1000);

    // --- Used by the app -----------------------------------------------------------------------------

    window.oncChat = {
        start(newSettings) {
            Object.assign(settings, newSettings);
            if (started || !channel) return;
            started = true;
            connect();
        },

        // Settings saved while the chat is open (see MainWindow.LiveSettings.cs)
        apply(newSettings) {
            Object.assign(settings, newSettings);
        },

        // A line from the app in the user's color, like a /me message (channel point redemptions)
        addAction(name, color, text) {
            const login = name.toLowerCase();
            addLine({ login, name: name || login, color: userColor(login, color), action: true, parts: [text] });
        },

        // For the app's watchdog: "open:<ms since Twitch last sent something>" or the state
        health() {
            if (!started) return 'missing';
            if (socket && socket.readyState === WebSocket.OPEN) return 'open:' + (Date.now() - lastData);
            return 'disconnected';
        }
    };
})();
