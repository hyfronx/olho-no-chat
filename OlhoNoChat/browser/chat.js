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
    const MAX_LINES_SCROLLED_BACK = 1000; // while scrolled back to read, see "Scrolling"

    const params = new URLSearchParams(location.search);
    const channel = (params.get('canal') || '').toLowerCase();
    const darkTheme = params.get('tema') === 'padrao';
    if (darkTheme)
        document.body.classList.add('tema-padrao');

    const box = document.getElementById('chat_box');

    // From the app (KapChat.GetMessageSettingsJson)
    let settings = {
        fade: 0, hideBots: true, playSound: false,
        highlightUsers: false, allowedUsersOnly: false, filterAllowAllVIPs: false, filterAllowAllMods: false,
        vips: [], blockList: []
    };
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
                if (message.tags['room-id']) loadChannelEmotes(message.tags['room-id']);
                break;
            case 'PRIVMSG':
                if (heldMessages) heldMessages.push(message);
                else onChatMessage(message);
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
        showLine({
            id: tags.id,
            login,
            name: tags['display-name'] || login,
            color: userColor(login, tags.color),
            action,
            parts: addOtherEmotes(splitTwitchEmotes(text, tags.emotes))
        }, tags.badges || '');
    }

    // --- Badges --------------------------------------------------------------------------------------
    // The "badges" tag of a message ("moderator/1,subscriber/12") names each badge; its picture comes from the
    // channel's own badges (subscriber, bits), then from the global ones. The global ones ship with the app
    // (badges-globais.json, made by ferramentas\atualizar_badges.ps1); the channel's come from ivr.fi, a public
    // community API. A badge not found in either is left out; subscriber falls back to the default one.
    // A line shown before the badges arrived gets them when they do.

    const badgeSets = { global: {}, channel: {} };
    const BADGE_IMAGE_ID = /\/badges\/v1\/([0-9a-f-]+)\//;

    fetch('badges-globais.json')
        .then(response => response.json())
        .then(sets => { badgeSets.global = sets; refreshBadges(); })
        .catch(e => console.warn('[ONC] Could not load the global badges', e));

    if (channel) {
        fetch('https://api.ivr.fi/v2/twitch/badges/channel?login=' + encodeURIComponent(channel))
            .then(response => response.ok ? response.json() : [])
            .then(list => {
                const sets = {};
                (Array.isArray(list) ? list : []).forEach(set => (set.versions || []).forEach(version => {
                    const m = BADGE_IMAGE_ID.exec(version.image_url_1x || '');
                    if (m) (sets[set.set_id] ??= {})[version.id] = m[1];
                }));
                badgeSets.channel = sets;
                refreshBadges();
            })
            .catch(e => console.warn('[ONC] Could not load the channel badges', e));
    }

    function badgeImageId(set, version) {
        return badgeSets.channel[set]?.[version] || badgeSets.global[set]?.[version]
            || (set === 'subscriber' ? badgeSets.global.subscriber?.['0'] : null);
    }

    function fillBadges(span) {
        span.replaceChildren();
        span.dataset.badges.split(',').forEach(badge => {
            const [set, version] = badge.split('/');
            const imageId = badgeImageId(set, version);
            if (!imageId) return;
            const base = 'https://static-cdn.jtvnw.net/badges/v1/' + imageId + '/';
            const img = document.createElement('img');
            img.className = 'tag ' + set + '-' + version;
            img.src = base + '1';
            img.srcset = base + '2 2x, ' + base + '3 4x';
            img.alt = set;
            span.append(img);
        });
    }

    function refreshBadges() {
        box.querySelectorAll('.badges[data-badges]').forEach(fillBadges);
    }

    // The "Filtros" of the app: blocked users, "only the listed users" (plus all VIPs / Mods, from the
    // badges tag "vip/1,subscriber/12") and their highlight. Also decides if the new-message sound rings.
    function showLine(line, badges) {
        if (settings.blockList.includes(line.login)) return;

        const isListed = settings.vips.includes(line.login);
        let byBadge = '';
        if (settings.filterAllowAllVIPs && /(?:^|,)vip\//.test(badges)) byBadge = 'VIP';
        if (settings.filterAllowAllMods && /(?:^|,)moderator\//.test(badges)) byBadge = 'Mod'; // Mod wins over VIP
        const allowed = isListed || byBadge !== '';

        if (settings.allowedUsersOnly && !allowed) return;
        line.highlight = settings.highlightUsers && allowed ? 'highlight' + byBadge : '';
        line.badges = badges;

        // The app decides if it rings (not while the sound is playing, "Quando tocar")
        if (settings.playSound && ((!settings.highlightUsers && !settings.allowedUsersOnly) || allowed))
            postToApp('onc:play-sound');

        addLine(line);
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

    // --- Emotes of BetterTTV, FrankerFaceZ and 7TV ---------------------------------------------------
    // Words of a message that are emotes of those sites become pictures. The channel's emotes win over the
    // global ones; with the same name on two sites: 7TV, then BTTV, then FFZ. Public addresses, no key needed.

    const emoteSets = { channel: [new Map(), new Map(), new Map()], global: [new Map(), new Map(), new Map()] };
    const SEVEN_TV = 0, BTTV = 1, FFZ = 2;

    // BTTV's overlay emotes (drawn over the emote before them); 7TV marks its own with flag 1
    const BTTV_ZERO_WIDTH = new Set(['cvHazmat', 'cvMask', 'IceCold', 'SoSnowy', 'TopHat', 'SantaHat', 'ReinDeer', 'CandyCane']);

    const sevenTvEmote = e => {
        const base = 'https://cdn.7tv.app/emote/' + e.id + '/';
        return { code: e.name, src: base + '1x.webp', srcset: base + '2x.webp 2x, ' + base + '4x.webp 4x', zeroWidth: (e.flags & 1) === 1 };
    };
    const bttvEmote = e => {
        const base = 'https://cdn.betterttv.net/emote/' + e.id + '/';
        return { code: e.code, src: base + '1x', srcset: base + '2x 2x, ' + base + '3x 4x', zeroWidth: BTTV_ZERO_WIDTH.has(e.code) };
    };
    const ffzEmote = e => ({
        code: e.code,
        src: e.images['1x'],
        srcset: [e.images['2x'] && e.images['2x'] + ' 2x', e.images['4x'] && e.images['4x'] + ' 4x'].filter(Boolean).join(', ')
    });

    // "modifier" emotes of BTTV/FFZ change the emote next to them in their own extension; here they stay text
    function loadEmotes(url, target, list, toEmote) {
        return fetch(url)
            .then(response => response.ok ? response.json() : null) // 404: the channel doesn't use that site
            .then(data => (list(data) || []).forEach(e => {
                if (e.modifier) return;
                const emote = toEmote(e);
                if (emote.code && emote.src) target.set(emote.code, emote);
            }))
            .catch(e => console.warn('[ONC] Could not load the emotes from ' + url, e));
    }

    loadEmotes('https://7tv.io/v3/emote-sets/global', emoteSets.global[SEVEN_TV], d => d && d.emotes, sevenTvEmote);
    loadEmotes('https://api.betterttv.net/3/cached/emotes/global', emoteSets.global[BTTV], d => d, bttvEmote);
    loadEmotes('https://api.betterttv.net/3/cached/frankerfacez/emotes/global', emoteSets.global[FFZ], d => d, ffzEmote);

    // The chat's messages wait (up to 3 s) until the channel's emotes arrived, so the first ones get them too.
    // The channel id comes with the ROOMSTATE of the channel; its emotes are loaded once per page.
    let heldMessages = [];
    let channelEmotesFor = null;

    function loadChannelEmotes(roomId) {
        if (channelEmotesFor === roomId) return;
        channelEmotesFor = roomId;
        const id = encodeURIComponent(roomId);
        const loads = [
            loadEmotes('https://7tv.io/v3/users/twitch/' + id, emoteSets.channel[SEVEN_TV],
                d => d && d.emote_set && d.emote_set.emotes, sevenTvEmote),
            loadEmotes('https://api.betterttv.net/3/cached/users/twitch/' + id, emoteSets.channel[BTTV],
                d => d && (d.channelEmotes || []).concat(d.sharedEmotes || []), bttvEmote),
            loadEmotes('https://api.betterttv.net/3/cached/frankerfacez/users/twitch/' + id, emoteSets.channel[FFZ], d => d, ffzEmote)
        ];
        Promise.race([Promise.all(loads), new Promise(resolve => setTimeout(resolve, 3000))]).then(releaseHeldMessages);
    }

    function releaseHeldMessages() {
        if (!heldMessages) return;
        const messages = heldMessages;
        heldMessages = null;
        messages.forEach(onChatMessage);
    }

    function findEmote(word) {
        for (const sets of [emoteSets.channel, emoteSets.global]) {
            for (const set of sets) {
                const emote = set.get(word);
                if (emote) return emote;
            }
        }
        return null;
    }

    // Splits the text parts of a message at the emote words. A word may have punctuation around it ("LUL!").
    function addOtherEmotes(parts) {
        const result = [];
        parts.forEach(part => {
            if (typeof part !== 'string') {
                result.push(part);
                return;
            }
            let text = '';
            part.split(' ').forEach((word, i) => {
                if (i > 0) text += ' ';
                let emote = findEmote(word);
                let before = '', name = word, after = '';
                if (!emote) {
                    const m = /^([~!@#$%^&*()]*)(.+?)([~!@#$%^&*()]*)$/.exec(word);
                    if (m && (m[1] || m[3])) {
                        emote = findEmote(m[2]);
                        [before, name, after] = [m[1], m[2], m[3]];
                    }
                }
                if (!emote) {
                    text += word;
                    return;
                }
                if (text + before) result.push(text + before);
                result.push({ emote, name });
                text = after;
            });
            if (text) result.push(text);
        });
        return result;
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
        if (line.highlight) div.classList.add(line.highlight);
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
            if (!(part.emote.zeroWidth && stackOnPreviousEmote(message, img)))
                message.append(img);
        });
        linkify(message);

        div.append(time);
        if (line.badges) {
            const badges = document.createElement('span');
            badges.className = 'badges';
            badges.dataset.badges = line.badges;
            fillBadges(badges);
            div.append(badges);
        }
        div.append(nick);
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

    // Web addresses in a message become links (opened in the user's browser by the app, and only
    // while the borders are visible: the app sets "onc-links-on", see MainWindow.Links.cs)
    const LINK = /\b(?:https?:\/\/|www\.)[^\s<>"]+[^\s<>".,:;!?)\]'}]/gi;

    function linkify(root) {
        // Quick check on the whole text first (no \b here: an emote between two words joins their text)
        if (!/https?:\/\/|www\./i.test(root.textContent)) return;
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const texts = [];
        while (walker.nextNode()) texts.push(walker.currentNode);
        texts.forEach(node => {
            const text = node.nodeValue;
            const parts = document.createDocumentFragment();
            let last = 0;
            for (const match of text.matchAll(LINK)) {
                parts.append(text.slice(last, match.index));
                const a = document.createElement('a');
                a.className = 'onc-link';
                a.href = /^www\./i.test(match[0]) ? 'https://' + match[0] : match[0];
                a.target = '_blank';
                a.rel = 'noopener noreferrer';
                a.textContent = match[0];
                parts.append(a);
                last = match.index + match[0].length;
            }
            if (last === 0) return;
            parts.append(text.slice(last));
            node.replaceWith(parts);
        });
    }

    // An overlay emote (a hat, snow...) is drawn over the emote right before it, if there is one
    function stackOnPreviousEmote(message, img) {
        const isEmote = node => node && node.nodeType === Node.ELEMENT_NODE &&
            (node.classList.contains('emoticon') || node.classList.contains('onc-stack'));
        let previous = message.lastChild;
        if (previous && previous.nodeType === Node.TEXT_NODE && !previous.nodeValue.trim() && isEmote(previous.previousSibling)) {
            previous.remove();
            previous = message.lastChild;
        }
        if (!isEmote(previous)) return false;

        let stack = previous;
        if (!stack.classList.contains('onc-stack')) {
            stack = document.createElement('span');
            stack.className = 'onc-stack';
            previous.replaceWith(stack);
            stack.append(previous);
        }
        img.classList.add('onc-overlay');
        stack.append(img);
        return true;
    }

    function removeLines(selector) {
        box.querySelectorAll(selector).forEach(line => line.remove());
    }

    // --- Scrolling -----------------------------------------------------------------------------------
    // The newest message stays in view ("pinned"). In "modo rolagem" (the window takes clicks) the chat can be
    // scrolled back: new messages then don't pull it down, and old lines are kept longer while being read.

    let pinned = true;
    const isAtBottom = () => box.scrollHeight - box.scrollTop - box.clientHeight < 4;
    const scrollToBottom = () => { box.scrollTop = box.scrollHeight; };

    const scrollBanner = document.createElement('div');
    scrollBanner.id = 'onc-scroll-banner';
    scrollBanner.className = 'onc-pill';
    scrollBanner.addEventListener('click', () => postToApp('onc:exit-scroll-mode'));
    const newMessages = document.createElement('div');
    newMessages.id = 'onc-new-messages';
    newMessages.className = 'onc-pill';
    newMessages.textContent = '↓ Novas mensagens';
    newMessages.addEventListener('click', pin);
    document.body.append(scrollBanner, newMessages);

    function pin() {
        pinned = true;
        newMessages.style.display = 'none';
        scrollToBottom();
    }

    // Only the user unpins the chat (the wheel, or dragging the scroll bar): lines taken out at the top also
    // move the scroll position, and that must not stop the chat from following the new messages.
    let dragging = false;
    box.addEventListener('wheel', (e) => {
        if (e.deltaY < 0 && box.scrollTop > 0) pinned = false;
    }, { passive: true });
    box.addEventListener('pointerdown', () => { dragging = true; });
    window.addEventListener('pointerup', () => { dragging = false; });

    box.addEventListener('scroll', () => {
        if (isAtBottom()) pin();
        else if (dragging) pinned = false;
    });

    // Old lines are taken out and the newest one scrolled into view once per frame, even in a busy chat
    let layoutPending = false;
    function scheduleLayout() {
        if (layoutPending) return;
        layoutPending = true;
        requestAnimationFrame(() => {
            layoutPending = false;
            let extra = box.children.length - (pinned ? MAX_LINES : MAX_LINES_SCROLLED_BACK);
            while (extra-- > 0) box.firstElementChild.remove();
            if (pinned) scrollToBottom();
            else newMessages.style.display = 'block';
        });
    }

    // Emotes load after their line is added and make it taller
    box.addEventListener('load', () => { if (pinned) scrollToBottom(); }, true);
    window.addEventListener('resize', () => { if (pinned) scrollToBottom(); });

    // Called by the app (MainWindow.UpdateChatScrollMode), also before this script ran (oncScrollModeWanted)
    window.oncSetScrollMode = function (mode) {
        box.style.overflowY = mode.enabled ? 'auto' : 'hidden';
        scrollBanner.textContent = 'Modo rolagem: use a rodinha do mouse\n' +
            (mode.hotkey ? 'Clique aqui ou aperte ' + mode.hotkey + ' para sair' : 'Clique aqui para sair');
        scrollBanner.style.display = mode.enabled && mode.banner ? 'block' : 'none';
        if (!mode.enabled) pin();
    };
    if (window.oncScrollModeWanted) window.oncSetScrollMode(window.oncScrollModeWanted);

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
            showLine({ login, name, color: userColor(login, color), action: true, parts: addOtherEmotes([text]) }, '');
        },

        // For the app's watchdog: "open:<ms since Twitch last sent something>" or the state
        health() {
            if (!started) return 'missing';
            if (socket && socket.readyState === WebSocket.OPEN) return 'open:' + (Date.now() - lastData);
            return 'disconnected';
        }
    };
})();
