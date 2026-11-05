// The "Padrão" chat: reads the chat of a Twitch channel (anonymous IRC, read-only) and shows its messages.
// Opened by the app as chat.html?canal=<name>&tema=padrao (an empty channel in the Chat Multiplataforma: only YouTube,
// Kick and/or TikTok, without Twitch); it connects when the app calls
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
    const LINE_BATCH_MS = 200;

    const params = new URLSearchParams(location.search);
    const channel = (params.get('canal') || '').toLowerCase();
    const darkTheme = params.get('tema') === 'padrao';
    if (darkTheme)
        document.body.classList.add('tema-padrao');

    const box = document.getElementById('chat_box');

    // From the app (PadraoChat.GetMessageSettingsJson)
    let settings = {
        fade: 0, hideBots: true, hideGifs: false, hideOtherChannels: false, playSound: false,
        twitchNotices: true, youTubeNotices: true, tiktokGifts: true, tiktokFollows: true, tiktokShares: false, tiktokSuperFans: true,
        highlightUsers: false, allowedUsersOnly: false, filterAllowAllVIPs: false, filterAllowAllMods: false,
        vips: [], blockList: [], multiplatform: false, kick: '',
        // Pause: the speed when continuing (one-line messages a second; 0 = automatic) and the hotkey, for the marker
        resumeSpeed: 0, pauseHotkey: ''
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
            case 'USERNOTICE':
                if (heldMessages) heldMessages.push(message);
                else if (message.command === 'PRIVMSG') onChatMessage(message);
                else onTwitchNotice(message);
                break;
            case 'CLEARCHAT':
                // A timeout or ban takes that user's messages out; without a user, /clear empties the chat
                if (message.params[1]) removeLines(`.chat_line[data-nick="${CSS.escape(message.params[1].toLowerCase())}"]`);
                else {
                    // Paused, the marker stays: the pause goes on with the messages that come after the /clear
                    const kept = pause === 'paused' && marker ? [marker] : [];
                    if (!kept.length) marker = null;
                    box.replaceChildren(...kept);
                    newLines.replaceChildren();
                    pauseLost = false;
                    refreshMarker();
                }
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
        postToApp('onc:chat-state:' + (newState === 'unavailable' ? 'disconnected' : newState));
        if (settings.multiplatform) {
            setPlatformState('twitch', newState === 'unavailable' ? 'notfound' : newState === 'disconnected' ? 'failed' : newState);
            return;
        }
        showPill(newState, stateTexts[newState], newState === 'connected' ? 4000 : 0);
    }

    function showPill(className, text, fadeAfterMs) {
        if (!pill.isConnected) document.body.appendChild(pill);
        pill.className = className;
        pillText.textContent = text;
        clearTimeout(pillFadeTimer);
        if (fadeAfterMs) pillFadeTimer = setTimeout(() => pill.classList.add('faded'), fadeAfterMs);
    }

    // Chat Multiplataforma: one pill for all the chats in use, not only Twitch's. Twitch and Kick are read here; the app
    // tells the YouTube and TikTok states (oncChat.platformState). Each one is connecting, connected, waiting (YouTube
    // and TikTok: the channel isn't live), failed (no connection, trying again), notfound or off.
    const PLATFORM_NAMES = { twitch: 'Twitch', youtube: 'YouTube', kick: 'Kick', tiktok: 'TikTok' };
    const PLATFORM_PROBLEMS = { waiting: 'esperando a live', failed: 'sem conexão', notfound: 'canal não encontrado' };
    const platformStates = {};
    let multiPillText = '';

    function setPlatformState(platform, newState) {
        platformStates[platform] = newState;
        showMultiPill();
    }

    function showMultiPill() {
        if (!settings.multiplatform) return;
        const used = Object.keys(PLATFORM_NAMES).filter(p => platformStates[p] && platformStates[p] !== 'off');
        if (!used.length) {
            pill.remove();
            multiPillText = '';
            return;
        }
        const connected = used.filter(p => platformStates[p] === 'connected').length;
        const problems = used.filter(p => PLATFORM_PROBLEMS[platformStates[p]]);
        let className, text, fadeAfterMs;
        if (used.some(p => platformStates[p] === 'connecting')) {
            className = 'connecting';
            text = used.length === 1 ? 'Conectando ao chat…' : 'Conectando aos chats…';
            fadeAfterMs = 0;
        } else if (!problems.length) {
            className = 'connected';
            text = used.length === 1 ? 'Conectado ao chat' : `Conectado aos ${used.length} chats`;
            fadeAfterMs = 4000;
        } else {
            // Waiting for a live isn't a failure: gray; a lost or wrong channel: red. It stays a while, then the dots
            // of the channel strip keep telling it
            className = problems.some(p => platformStates[p] !== 'waiting') ? 'disconnected' : 'waiting';
            const parts = problems.map(p => PLATFORM_NAMES[p] + ': ' + PLATFORM_PROBLEMS[platformStates[p]]);
            if (connected) parts.unshift(`Conectado a ${connected} de ${used.length} chats`);
            text = parts.join(' · ');
            fadeAfterMs = 10000;
        }
        // The same text again (a state that didn't change for the pill) doesn't show the pill again
        if (text === multiPillText && pill.className.split(' ')[0] === className) return;
        multiPillText = text;
        showPill(className + ' multi', text, fadeAfterMs);
    }

    // --- Messages ------------------------------------------------------------------------------------

    function onChatMessage(message) {
        if (message.params[0] !== '#' + channel) return;
        const login = message.prefix.split('!')[0];
        let text = message.params[1] || '';
        const tags = message.tags;

        if (settings.hideBots && (text[0] === '!' || isTwitchBot(login, tags.badges))) return;

        const sourceRoom = otherChannelRoom(tags);
        if (sourceRoom && settings.hideOtherChannels) return;

        let action = false;
        const actionMatch = /^\x01ACTION (.*)\x01$/.exec(text);
        if (actionMatch) {
            action = true;
            text = actionMatch[1];
        }

        // The text of a channel point redemption: once only, if the app already showed it (see "Redemptions")
        const name = tags['display-name'] || login;
        const reward = tags['custom-reward-id'] ? { by: name.toLowerCase(), text: text.trim() } : null;
        if (reward) {
            const shown = unpairedRewardLine(reward, true);
            if (shown) {
                shown.dataset.paired = '';
                return;
            }
        }

        let parts = splitAtPlaces(text, [...twitchEmotePlaces(tags.emotes), ...gifPlaces(tags.gifs)]);
        if (tags.bits) parts = addCheers(parts);

        showLine({
            id: tags.id,
            sourceRoom,
            login,
            name,
            color: userColor(login, tags.color),
            reward,
            action,
            parts: addOtherEmotes(parts)
        }, tags.badges || '');
    }

    // Shared chat: the channel a message or notice came from, when it is another channel of the session
    function otherChannelRoom(tags) {
        return tags['source-room-id'] && tags['source-room-id'] !== tags['room-id'] ? tags['source-room-id'] : '';
    }

    // --- Redemptions --------------------------------------------------------------------------------
    // A channel point redemption whose reward asks for a text comes twice in the account's own channel: Twitch sends
    // the text as a chat message (tag "custom-reward-id") and the app sends it under "resgatou ..." (addAction, from
    // EventSub). It shows once, as the app's line, right under the redemption. Either can come first, so each one
    // looks for its pair among the lines of the last 30 s (same viewer, same text); a line pairs only once. Without
    // the app's lines (another channel, redemptions off) the chat message shows as always.

    const REWARD_PAIR_MS = 30000;

    // reward: { by: the viewer's name in lowercase (EventSub gives the display name), text }; appLine: look for the
    // app's line (true) or the chat message (false)
    function unpairedRewardLine(reward, appLine) {
        const since = Date.now() - REWARD_PAIR_MS;
        const selector = `.chat_line[data-reward-by="${CSS.escape(reward.by)}"]:not([data-paired])`;
        return [...box.querySelectorAll(selector), ...newLines.querySelectorAll(selector)].find(line =>
            line.dataset.reward === reward.text && line.classList.contains('onc-redemption') === appLine
            && Number(line.dataset.timestamp) >= since);
    }

    // --- Twitch notices ------------------------------------------------------------------------------
    // Subs, gifts, raids, announcements, watch streaks... come as USERNOTICE: the kind in "msg-id", the numbers in
    // "msg-param-..." tags and, when the viewer wrote something with it, their message. Twitch's own text
    // ("system-msg") is always in English: the line says it with the Portuguese words of the Twitch site, like the
    // YouTube member notices. A kind not known here shows Twitch's text, so nothing goes missing.

    const ANONYMOUS_GIFTER = 'ananonymousgifter';

    function onTwitchNotice(message) {
        if (!settings.twitchNotices || message.params[0] !== '#' + channel) return;
        const tags = message.tags;
        // In a shared chat, a notice of another channel of the session says in "source-msg-id" what it was
        const kind = tags['msg-id'] === 'sharedchatnotice' ? tags['source-msg-id'] : tags['msg-id'];
        // A cancelled raid only matters to the channel that started it
        if (kind === 'unraid') return;
        // Each sub of a "gifting 5 subs to the community" comes too: the line of the whole gift is enough
        if (kind === 'subgift' && tags['msg-param-community-gift-id']) return;

        const login = tags.login || '';
        const text = message.params[1] || '';
        const notice = login && noticeText(kind, tags);
        if (!notice) return;
        // Bots make announcements too (Nightbot timers)
        if (settings.hideBots && kind === 'announcement' && (text[0] === '!' || isTwitchBot(login, tags.badges))) return;
        const sourceRoom = otherChannelRoom(tags);
        if (sourceRoom && settings.hideOtherChannels) return;

        showLine({
            id: tags.id,
            sourceRoom,
            login,
            name: login === ANONYMOUS_GIFTER ? 'Anônimo' : tags['display-name'] || login,
            color: userColor(login, tags.color),
            action: false,
            parts: addOtherEmotes(splitAtPlaces(text, twitchEmotePlaces(tags.emotes))),
            notice
        }, tags.badges || '');
    }

    // What the line says after the name, as the Twitch site says it in Portuguese: texts, and the values that change
    // (numbers, names, the plan) as { value }, which stand out (see addLine)
    function noticeText(kind, tags) {
        const param = name => tags['msg-param-' + name] || '';
        const count = name => Number(param(name)) || 0;
        const value = v => ({ value: String(v) });
        const word = (n, one, many) => n === 1 ? one : many;
        const tier = { 1000: 1, 2000: 2, 3000: 3 }[param('sub-plan')];
        const plan = param('sub-plan') === 'Prime' ? [' com o ', value('Prime')] : tier ? [' no ', value('Grupo ' + tier)] : [];

        switch (kind) {
            case 'sub': {
                const advance = count('multimonth-duration');
                return ['se inscreveu', ...plan, ...(advance > 1 ? [' com ', value(advance), ' meses de antecedência!'] : ['!'])];
            }
            case 'resub': {
                const months = count('cumulative-months');
                return ['se inscreveu', ...plan,
                    ...(months > 0 ? ['. A inscrição completou ', value(months), word(months, ' mês!', ' meses!')] : ['!'])];
            }
            case 'subgift': {
                const recipient = value(param('recipient-display-name') || param('recipient-user-name'));
                const months = count('gift-months');
                if (months > 1) return ['deu ', value(months), ' meses de presente para ', recipient, '!'];
                return ['deu uma inscrição', ...(tier > 1 ? [' do ', value('Grupo ' + tier)] : []), ' de presente para ', recipient, '!'];
            }
            case 'submysterygift': {
                const gifts = count('mass-gift-count');
                return ['está dando ', value(gifts), word(gifts, ' inscrição', ' inscrições') + ' de presente para a comunidade!'];
            }
            case 'giftpaidupgrade':
                return ['deu continuidade à inscrição de presente que recebeu de ', value(param('sender-name') || param('sender-login')), '!'];
            case 'anongiftpaidupgrade':
                return ['deu continuidade à inscrição de presente que recebeu de um usuário anônimo!'];
            case 'primepaidupgrade':
                return ['converteu a inscrição Prime para uma inscrição paga!'];
            case 'standardpayforward':
            case 'communitypayforward': {
                const from = param('prior-gifter-anonymous') === 'true'
                    ? 'um presenteador anônimo' : value(param('prior-gifter-display-name') || param('prior-gifter-user-name'));
                const to = kind === 'standardpayforward' && (param('recipient-display-name') || param('recipient-user-name'));
                return ['retribuiu o presente que recebeu de ', from, ' e presenteou ', to ? value(to) : 'a comunidade', '!'];
            }
            case 'raid':
                return ['está fazendo uma raid com um grupo de ', value(count('viewerCount')), '.'];
            case 'announcement':
                return ['fez um comunicado:'];
            case 'bitsbadgetier':
                return ['conquistou um novo distintivo de ', value(count('threshold')), ' Bits!'];
            case 'viewermilestone':
                if (param('category') === 'watch-streak') {
                    const streams = count('value');
                    return ['está com uma sequência de ', value(streams), word(streams, ' transmissão!', ' transmissões!')];
                }
                break;
        }
        // Twitch's text, without the name the line already starts with
        const system = tags['system-msg'] || '';
        const name = tags['display-name'] || tags.login || '';
        return name && system.toLowerCase().startsWith(name.toLowerCase() + ' ') ? system.slice(name.length + 1) : system;
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
            const img = set === 'kicksub' ? kickSubBadge(Number(version)) : twitchBadge(set, version);
            if (!img) return;
            img.className = 'tag ' + set + '-' + version;
            img.alt = set;
            span.append(img);
        });
    }

    function twitchBadge(set, version) {
        const imageId = badgeImageId(set, version);
        if (!imageId) return null;
        const base = 'https://static-cdn.jtvnw.net/badges/v1/' + imageId + '/';
        const img = document.createElement('img');
        img.src = base + '1';
        img.srcset = base + '2 2x, ' + base + '3 4x';
        return img;
    }

    function refreshBadges() {
        box.querySelectorAll('.badges[data-badges]').forEach(fillBadges);
        newLines.querySelectorAll('.badges[data-badges]').forEach(fillBadges);
    }

    // --- Shared chat ("chat unido") -----------------------------------------------------------------
    // While channels share their chats, a message typed in another channel carries that channel's id
    // ("source-room-id"). Its line gets a bar in a color of that channel and, like on Twitch, starts with the
    // channel's picture. The name and picture come from ivr.fi (the community API of the channel badges), once
    // per channel. A line shown before the picture arrived gets it when it does; without it (API down) the line
    // still has its bar.

    const sourceChannels = new Map(); // room id -> { name, picture } once known, null while loading or failed

    // One color per guest channel, in the order they show up: two channels of a session never share one
    const SOURCE_COLORS = ['#9146FF', '#00C8AF', '#FFB31A', '#FF5E7A', '#3FA9F5'];
    const sourceColors = new Map();

    function sourceColor(room) {
        if (!sourceColors.has(room)) sourceColors.set(room, SOURCE_COLORS[sourceColors.size % SOURCE_COLORS.length]);
        return sourceColors.get(room);
    }

    function fillSource(span) {
        const room = span.dataset.room;
        if (!sourceChannels.has(room)) loadSourceChannel(room);
        const info = sourceChannels.get(room);
        if (!info || span.firstChild) return;
        const img = document.createElement('img');
        img.src = info.picture;
        img.alt = img.title = info.name;
        span.append(img);
    }

    function loadSourceChannel(room) {
        sourceChannels.set(room, null);
        fetch('https://api.ivr.fi/v2/twitch/user?id=' + encodeURIComponent(room))
            .then(response => response.ok ? response.json() : [])
            .then(list => {
                const user = Array.isArray(list) ? list[0] : list;
                if (!user || !user.logo) return;
                // The small size of Twitch's profile pictures (the API gives the 600x600 one)
                sourceChannels.set(room, { name: user.displayName || user.login, picture: user.logo.replace('600x600', '70x70') });
                const selector = `.onc-source[data-room="${CSS.escape(room)}"]`;
                box.querySelectorAll(selector).forEach(fillSource);
                newLines.querySelectorAll(selector).forEach(fillSource);
            })
            .catch(e => console.warn('[ONC] Could not load the shared chat channel ' + room, e));
    }

    // The "Filtros" of the app: blocked users, "only the listed users" (plus all VIPs / Mods, from the
    // badges tag "vip/1,subscriber/12") and their highlight. Also decides if the new-message sound rings.
    // The badges shown can differ from the ones filtered (a YouTube moderator counts as a Mod, without a Twitch badge).
    function showLine(line, badges, shownBadges = badges) {
        if (settings.blockList.includes(line.login)) return;

        const isListed = settings.vips.includes(line.login);
        let byBadge = '';
        if (settings.filterAllowAllVIPs && /(?:^|,)vip\//.test(badges)) byBadge = 'VIP';
        if (settings.filterAllowAllMods && /(?:^|,)moderator\//.test(badges)) byBadge = 'Mod'; // Mod wins over VIP
        const allowed = isListed || byBadge !== '';

        if (settings.allowedUsersOnly && !allowed) return;
        line.highlight = settings.highlightUsers && allowed ? 'highlight' + byBadge : '';
        line.badges = shownBadges;

        // The app decides if it rings (not while the sound is playing, "Quando tocar")
        if (settings.playSound && ((!settings.highlightUsers && !settings.allowedUsersOnly) || allowed))
            postToApp('onc:play-sound');

        addLine(line);
    }

    // The "emotes" tag ("25:0-4,12-16/1902:6-10") gives each emote's place counted in characters
    // (code points, so an emoji before an emote counts as one)
    function twitchEmotePlaces(emotesTag) {
        const places = [];
        (emotesTag || '').split('/').forEach(entry => {
            const [id, ranges] = entry.split(':');
            if (!ranges) return;
            ranges.split(',').forEach(range => {
                const [first, last] = range.split('-').map(Number);
                places.push({ first, last, part: name => ({ emote: twitchEmoteUrls(id), name }) });
            });
        });
        return places;
    }

    // GIFs (GIPHY, sent by tier 2 and 3 subscribers since September 2026): the "gifs" tag gives each one's
    // place the same way, then its id and its address ("0-33|<id>|https://media4.giphy.com/..."). The text
    // there is the GIF's title in brackets. Twitch asks for the address exactly as it comes.
    function gifPlaces(gifsTag) {
        const places = [];
        (gifsTag || '').split(/,(?=\d+-\d+\|)/).forEach(entry => {
            const m = /^(\d+)-(\d+)\|[^|]*\|(https:\/\/\S+)$/.exec(entry);
            if (m) places.push({ first: Number(m[1]), last: Number(m[2]), part: name => ({ gif: m[3], name }) });
        });
        return places;
    }

    // "Esconder GIFs" (Configurações > Chat): only their title, also on the lines already shown
    function showOrHideGifs() {
        document.body.classList.toggle('onc-hide-gifs', !!settings.hideGifs);
    }

    // The message split at the places of its emotes and GIFs; the text around them stays text
    function splitAtPlaces(text, places) {
        if (!places.length) return [text];
        places.sort((a, b) => a.first - b.first);

        const chars = Array.from(text);
        const parts = [];
        let next = 0;
        places.forEach(place => {
            if (place.first < next || place.last >= chars.length) return;
            if (place.first > next) parts.push(chars.slice(next, place.first).join(''));
            parts.push(place.part(chars.slice(place.first, place.last + 1).join('')));
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

    // --- Cheers (Bits) -------------------------------------------------------------------------------
    // A message with Bits (tag "bits") has words like "Cheer100": a cheermote prefix and the Bits it gives. Each one
    // becomes the cheermote's picture (animated) for the highest tier it reaches, then the number in the tier's color,
    // like on Twitch. The global cheermotes and the channel's own come from Twitch's public GraphQL (the address its
    // own site uses, no login); the picture's address is built from the group's template, with the prefix in lowercase.

    const cheermotes = new Map(); // lowercase prefix -> { template, tiers (highest first) }
    let cheerColors = [];         // [{ bits, color }], highest first

    function loadCheermotes(roomId) {
        const query = '{ cheerConfig { displayConfig { colors { bits color } } groups { templateURL nodes { prefix tiers { bits } } } } '
            + 'user(id: ' + JSON.stringify(roomId) + ') { cheer { cheerGroups { templateURL nodes { prefix tiers { bits } } } } } }';
        return fetch('https://gql.twitch.tv/gql', {
            method: 'POST',
            headers: { 'Client-Id': 'kimne78kx3ncx6brgo4mv6wki5h1ko' },
            body: JSON.stringify({ query })
        })
            .then(response => response.ok ? response.json() : null)
            .then(result => {
                const data = result && result.data;
                if (!data) return;
                cheerColors = ((data.cheerConfig && data.cheerConfig.displayConfig.colors) || []).sort((a, b) => b.bits - a.bits);
                const groups = [...((data.cheerConfig && data.cheerConfig.groups) || []),
                                ...((data.user && data.user.cheer && data.user.cheer.cheerGroups) || [])];
                groups.forEach(group => (group.nodes || []).forEach(node => cheermotes.set(node.prefix.toLowerCase(), {
                    template: group.templateURL,
                    tiers: (node.tiers || []).map(t => t.bits).sort((a, b) => b - a)
                })));
            })
            .catch(e => console.warn('[ONC] Could not load the cheermotes', e));
    }

    function cheerPart(word) {
        const m = /^([a-z]+?)(\d+)$/i.exec(word);
        const cheermote = m && cheermotes.get(m[1].toLowerCase());
        const bits = m ? Number(m[2]) : 0;
        const tier = cheermote && cheermote.tiers.find(t => t <= bits);
        if (!tier) return null;
        const url = scale => cheermote.template.replace('PREFIX', m[1].toLowerCase()).replace('BACKGROUND', darkTheme ? 'dark' : 'light')
            .replace('ANIMATION', 'animated').replace('TIER', tier).replace('SCALE', scale).replace('EXTENSION', 'gif');
        const color = (cheerColors.find(c => c.bits <= bits) || {}).color;
        return { emote: { src: url(1), srcset: url(2) + ' 2x, ' + url(4) + ' 4x' }, name: word, cheer: { bits, color } };
    }

    // Splits the text parts of a message with Bits at its cheer words
    function addCheers(parts) {
        const result = [];
        parts.forEach(part => {
            if (typeof part !== 'string') {
                result.push(part);
                return;
            }
            let text = '';
            part.split(' ').forEach((word, i) => {
                if (i > 0) text += ' ';
                const cheer = cheerPart(word);
                if (!cheer) {
                    text += word;
                    return;
                }
                if (text) result.push(text);
                result.push(cheer);
                text = '';
            });
            if (text) result.push(text);
        });
        return result;
    }

    // --- Bots ----------------------------------------------------------------------------------------
    // "Esconder bots e comandos": on Twitch a bot is an account with Twitch's own "Chat Bot" badge (bot-badge, given to
    // bots that send through the chat API) or one on FrankerFaceZ's public list of known bots (Nightbot,
    // StreamElements, Moobot...). Not the name: a person can be called "...bot". YouTube and Kick have neither, so
    // there the name ending in "bot" is still the sign.
    const knownBots = new Set();
    const knownBotsLoad = fetch('https://api.frankerfacez.com/v1/badge/bot')
        .then(response => response.ok ? response.json() : null)
        .then(data => Object.values((data && data.users) || {}).flat().forEach(login => knownBots.add(String(login).toLowerCase())))
        .catch(e => console.warn('[ONC] Could not load the list of known bots', e));

    function isTwitchBot(login, badges) {
        return /(?:^|,)bot-badge\//.test(badges || '') || knownBots.has(login);
    }

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
            loadEmotes('https://api.betterttv.net/3/cached/frankerfacez/users/twitch/' + id, emoteSets.channel[FFZ], d => d, ffzEmote),
            loadCheermotes(roomId)
        ];
        // The list of known bots too, so the first messages of a bot are hidden as well
        Promise.race([Promise.all([...loads, knownBotsLoad]), new Promise(resolve => setTimeout(resolve, 3000))]).then(releaseHeldMessages);
    }

    function releaseHeldMessages() {
        if (!heldMessages) return;
        const messages = heldMessages;
        heldMessages = null;
        messages.forEach(handleLine);
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

    // A name with something to see: not only spaces, invisible characters (zero width, unassigned like U+FFF4) or blank
    // letters (the Hangul filler ㅤ, the empty Braille cell). Some TikTok nicknames are made of these on purpose.
    const VISIBLE = /[^\p{White_Space}\p{Cc}\p{Cf}\p{Cn}\p{M}ᅟᅠㅤﾠ⠀]/u;

    // The name shown: an invisible one gives way to the login (the @ on TikTok and YouTube)
    function shownName(line) {
        return VISIBLE.test(line.name || '') || !line.login ? line.name : line.login;
    }

    function addLine(line) {
        newLines.appendChild(buildLine(line));
        scheduleLayout();
    }

    // One chat line: time, name, ":" and the message (a /me message has no ":" and is in the user's color)
    function buildLine(line) {
        const div = document.createElement('div');
        div.className = 'chat_line';
        if (line.highlight) div.classList.add(line.highlight);
        if (line.redemption) div.classList.add('onc-redemption');
        if (line.reward) {
            div.dataset.rewardBy = line.reward.by;
            div.dataset.reward = line.reward.text;
            if (line.reward.paired) div.dataset.paired = '';
        }
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
        nick.textContent = shownName(line);

        const message = document.createElement('span');
        message.className = 'message';
        if (line.action) message.style.color = line.color;
        line.parts.forEach(part => {
            if (typeof part === 'string') {
                message.append(part);
                return;
            }
            if (part.gif) {
                // Both are there, the setting shows one (see showOrHideGifs); a hidden GIF isn't even downloaded
                const title = document.createElement('span');
                title.className = 'onc-gif-title';
                title.textContent = part.name;
                const gif = document.createElement('img');
                gif.className = 'onc-gif';
                gif.loading = 'lazy';
                gif.src = part.gif;
                gif.alt = gif.title = part.name;
                message.append(title, gif);
                return;
            }
            const img = document.createElement('img');
            img.className = 'emoticon';
            img.src = part.emote.src;
            if (part.emote.srcset) img.srcset = part.emote.srcset;
            img.alt = img.title = part.name;
            if (!(part.emote.zeroWidth && stackOnPreviousEmote(message, img)))
                message.append(img);
            if (part.cheer) {
                const bits = document.createElement('span');
                bits.className = 'onc-cheer-bits';
                bits.textContent = part.cheer.bits;
                if (part.cheer.color) bits.style.color = part.cheer.color;
                message.append(bits);
            }
        });
        linkify(message);

        div.append(time);
        // Chat Multiplataforma: where the message came from (hidden by CSS while it is off, see showPlatforms)
        const platform = document.createElement('span');
        platform.className = 'onc-platform ' + (line.platform || 'twitch');
        platform.title = { youtube: 'YouTube', kick: 'Kick', tiktok: 'TikTok' }[line.platform] || 'Twitch';
        div.append(platform);
        if (line.sourceRoom) {
            const source = document.createElement('span');
            source.className = 'onc-source';
            source.dataset.room = line.sourceRoom;
            fillSource(source);
            div.classList.add('onc-other-channel');
            div.style.setProperty('--source-color', sourceColor(line.sourceRoom));
            div.append(source);
        }
        if (line.badges) {
            const badges = document.createElement('span');
            badges.className = 'badges';
            badges.dataset.badges = line.badges;
            fillBadges(badges);
            div.append(badges);
        }
        div.append(nick);
        // A notice ("virou membro do canal!" on YouTube, a sub or a raid on Twitch, a gift on TikTok) in the platform's
        // color, then the message if there is one (on its own line, see chat.css). Its values ({ value }) stand out; a
        // TikTok gift shows its picture ({ image, name }) like an emote.
        if (line.notice) {
            const notice = document.createElement('span');
            notice.className = 'onc-notice';
            [].concat(line.notice).forEach(part => {
                if (typeof part === 'string') {
                    notice.append(part);
                    return;
                }
                if (part.image) {
                    const img = document.createElement('img');
                    img.className = 'emoticon onc-notice-image';
                    img.src = part.image;
                    img.alt = img.title = part.name || '';
                    notice.append(img, ' ');
                    return;
                }
                const value = document.createElement('span');
                value.className = 'onc-notice-value';
                value.textContent = part.value;
                notice.append(value);
            });
            div.classList.add('onc-notice-line', 'onc-notice-' + (line.platform || 'twitch'));
            if (line.kind) div.classList.add('onc-' + line.platform + '-' + line.kind);
            div.append(' ', notice);
        } else if (!line.action) {
            const colon = document.createElement('span');
            colon.className = 'colon';
            colon.textContent = ':';
            div.append(colon);
        }
        // YouTube Super Chat: the amount in its color before the message
        if (line.superchat) {
            const amount = document.createElement('span');
            amount.className = 'onc-superchat-amount';
            amount.textContent = line.superchat.amount;
            div.classList.add('onc-superchat');
            div.style.setProperty('--superchat-color', line.superchat.color);
            message.prepend(amount, ' ');
        }
        div.append(' ', message);
        return div;
    }

    // --- Oversized characters ----------------------------------------------------------------------
    // Some characters are drawn much taller than a line (cuneiform signs like 𒐫, "zalgo" text with piles of accents...):
    // with the fixed line height they would cover the name and the messages around. A character keeps at most a few
    // accents (real writing needs no more; zalgo has dozens), then each unusual character (with its accents) is measured
    // once in the font of its place, and one that would leave its line is drawn smaller, just enough to fit. Lines of
    // common text (Latin letters and punctuation) skip all this.

    const COMMON_TEXT = /^[\u0000-\u024f\u2000-\u206f]*$/;
    // The room around the baseline in a line, in font sizes (the line height is 24px for 18px letters)
    const ROOM_UP = 1.0;
    const ROOM_DOWN = 0.32;
    const MAX_MARKS = 4; // accents on one character (Hindi or Tibetan clusters can have a few)
    const MARK = /\p{M}/gu;
    const glyphCanvas = document.createElement('canvas').getContext('2d');
    const graphemes = new Intl.Segmenter('pt-BR', { granularity: 'grapheme' });
    const glyphScales = new Map(); // "<font>|<character>" -> the size to draw it (1 = as it is)

    function glyphScale(grapheme, font) {
        const key = font + '|' + grapheme;
        let scale = glyphScales.get(key);
        if (scale === undefined) {
            glyphCanvas.font = font;
            const ink = glyphCanvas.measureText(grapheme);
            const up = ink.actualBoundingBoxAscent / 100;
            const down = ink.actualBoundingBoxDescent / 100;
            // A little over the room is fine (accented capitals, emoji, Tibetan stacks): only the oversized shrink
            scale = up > ROOM_UP * 1.1 || down > ROOM_DOWN * 1.4
                ? Math.min(ROOM_UP / up, ROOM_DOWN / Math.max(down, 0.01))
                : 1;
            if (glyphScales.size >= 3000) glyphScales.clear();
            glyphScales.set(key, scale);
        }
        return scale;
    }

    function fitOversized(line) {
        if (COMMON_TEXT.test(line.textContent)) return;
        const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
        const texts = [];
        while (walker.nextNode()) texts.push(walker.currentNode);
        texts.forEach(node => {
            const text = node.nodeValue;
            if (COMMON_TEXT.test(text)) return;
            const style = getComputedStyle(node.parentElement);
            const font = `${style.fontStyle} ${style.fontWeight} 100px ${style.fontFamily}`;
            const parts = document.createDocumentFragment();
            let plain = '';
            let small = null; // the run of oversized characters being drawn at the same size
            let changed = false;
            for (const { segment } of graphemes.segment(text)) {
                let marks = 0;
                const grapheme = segment.length > MAX_MARKS + 2 ? segment.replace(MARK, m => ++marks > MAX_MARKS ? '' : m) : segment;
                changed ||= grapheme !== segment;
                const scale = COMMON_TEXT.test(grapheme) ? 1 : glyphScale(grapheme, font);
                if (scale === 1) {
                    plain += grapheme;
                    small = null;
                    continue;
                }
                changed = true;
                const size = scale.toFixed(2) + 'em';
                if (small && small.style.fontSize === size) {
                    small.textContent += grapheme;
                    continue;
                }
                if (plain) parts.append(plain);
                plain = '';
                small = document.createElement('span');
                small.className = 'onc-fit';
                small.style.fontSize = size;
                small.textContent = grapheme;
                parts.append(small);
            }
            if (!changed) return;
            if (plain) parts.append(plain);
            node.replaceWith(parts);
        });
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
        newLines.querySelectorAll(selector).forEach(line => line.remove());
        refreshMarker(); // the count of a pause
        refreshReadingPill();
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
        if (pause === 'reading') wheelHoldUntil = performance.now() + WHEEL_HOLD_MS;
        if (e.deltaY < 0 && box.scrollTop > 0) pinned = false;
    }, { passive: true });
    box.addEventListener('pointerdown', () => { dragging = true; });
    window.addEventListener('pointerup', () => { dragging = false; });

    // Paused or reading, the pause decides (see "Pause"): reaching the bottom by hand doesn't end a pause
    box.addEventListener('scroll', () => {
        if (pause !== 'live') return;
        if (isAtBottom()) pin();
        else if (dragging) pinned = false;
    });

    // New lines go on screen together every LINE_BATCH_MS (then old lines are taken out and the newest one
    // scrolled into view): in a busy chat the page is drawn a few times a second, not once per message,
    // which keeps the GPU work beside the game low.
    const newLines = document.createDocumentFragment();
    let layoutPending = false;
    function scheduleLayout() {
        if (layoutPending) return;
        layoutPending = true;
        setTimeout(layoutLines, LINE_BATCH_MS);
    }

    function layoutLines() {
        layoutPending = false;
        const added = [...newLines.children];
        // Paused or reading, the lines in view stay where they are when old ones are taken out at the top
        const anchor = pause === 'live' ? null : firstLineInView();
        const anchorTop = anchor ? anchor.offsetTop : 0;
        const scrollTop = box.scrollTop;
        box.append(newLines);
        added.forEach(fitOversized); // needs the line on the page, to know its font
        let extra = box.children.length - (pinned && pause === 'live' ? MAX_LINES : MAX_LINES_SCROLLED_BACK);
        while (extra-- > 0) {
            let first = box.firstElementChild;
            if (first === marker) {
                // Paused, the marker stays: the oldest unread messages go instead
                if (pause === 'paused' && first.nextElementSibling) {
                    first = first.nextElementSibling;
                    pauseLost = true;
                } else {
                    marker = null;
                }
            }
            first.remove();
        }
        if (anchor && anchor.isConnected) {
            const wanted = scrollTop + anchor.offsetTop - anchorTop;
            if (Math.abs(box.scrollTop - wanted) > 1) box.scrollTop = wanted;
        }
        if (pause === 'reading') readSetTo = box.scrollTop; // not a move by the person: the slide goes on
        if (pause !== 'live') {
            refreshMarker();
            refreshReadingPill();
        } else if (pinned) scrollToBottom();
        else newMessages.style.display = 'block';
        recordArrivals(added);
    }

    // Emotes load after their line is added and make it taller
    box.addEventListener('load', () => { if (pinned && pause === 'live') scrollToBottom(); }, true);
    window.addEventListener('resize', () => { if (pinned && pause === 'live') scrollToBottom(); });

    // Called by the app (MainWindow.UpdateChatScrollMode), also before this script ran (oncScrollModeWanted)
    window.oncSetScrollMode = function (mode) {
        box.style.overflowY = mode.enabled ? 'auto' : 'hidden';
        scrollBanner.textContent = 'Modo rolagem: use a rodinha do mouse\n' +
            (mode.hotkey ? 'Clique aqui ou aperte ' + mode.hotkey + ' para sair' : 'Clique aqui para sair');
        scrollBanner.style.display = mode.enabled && mode.banner ? 'block' : 'none';
        // A paused chat stays paused when the borders hide (and reads on by itself)
        if (!mode.enabled && pause === 'live') pin();
    };

    // --- Pause ---------------------------------------------------------------------------------------
    // The app's pause button (or its hotkey), for a streamer busy with the game: the messages keep coming (and
    // ringing), only the chat stops moving. A marker goes under the last message wholly on screen ("Chat pausado
    // · 12 mensagens novas"); scrolled back, it stays at the bottom. Continuing brings the messages below into view
    // by themselves, from where the chat is to the newest one: each comes in with a short slide and stays still for
    // the time it takes to read it (still text is easier to read than moving text, and the page is drawn only while
    // it slides). At the newest one the chat follows the new ones again. Pausing while it reads stops it there.
    // The app shows the state on its button (onc:pause-state:paused|reading|live).

    // The automatic speed: a person reads chat at about this many characters a second, plus a moment to find each
    // message and its name; an emote counts as a short word and a GIF as a quick look (so short messages go by
    // faster, long ones slower). settings.resumeSpeed (one-line messages a second) replaces it with a fixed speed.
    const READ_CHARS_PER_SECOND = 25;
    const READ_SECONDS_PER_MESSAGE = 0.3;
    const READ_CHARS_PER_IMAGE = 3;
    const READ_SECONDS_PER_GIF = 1;
    const READ_MIN_LINES_PER_SECOND = 0.4;
    const READ_MAX_LINES_PER_SECOND = 8;
    // Faster than the messages come, or it would never reach the newest one
    const CATCH_UP_FACTOR = 1.5;
    const ARRIVALS_MS = 20000;
    // Messages read faster than this come in together (a busy chat doesn't slide all the time)
    const STEP_MIN_MS = 800;
    // The slide of each step: each frame covers part of the way left (about 170 ms in all), at most 60 frames a second
    const SLIDE_FRAME_MS = 15;
    const SLIDE_TIME_S = 0.045;
    // The wheel moves the chat while it reads: the reading goes on from there a moment later
    const WHEEL_HOLD_MS = 400;

    let pause = 'live';      // 'paused' or 'reading'
    let marker = null;       // the marker of the pause now (older ones are quiet dividers)
    let pauseLost = false;   // so many messages came while paused that the oldest unread ones didn't fit
    let readTimer = 0, readFrame = 0, readTarget = null, readLast = 0, readSetTo = 0;
    let readToTop = false;   // the step shows the beginning of a message taller than the chat (the rest comes next)
    let wheelHoldUntil = 0;
    const arrivals = [];     // [time, height] of the lines added in the last ARRIVALS_MS, for CATCH_UP_FACTOR
    const readingTimes = new WeakMap();

    // While reading: how many messages are still below the screen (a click goes straight to the newest)
    const readingPill = document.createElement('div');
    readingPill.id = 'onc-reading';
    readingPill.className = 'onc-pill';
    readingPill.addEventListener('click', goLive);
    document.body.append(readingPill);

    const timeNow = () => new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
    const messagesText = (n) => n + (n === 1 ? ' mensagem nova' : ' mensagens novas');

    function firstLineInView() {
        for (const line of box.children)
            if (line.offsetTop + line.offsetHeight > box.scrollTop) return line;
        return null;
    }

    function newMarker() {
        const div = document.createElement('div');
        div.className = 'onc-pause-marker';
        div.dataset.time = timeNow();
        div.innerHTML = '<div class="onc-pause-label"><div class="onc-pause-title"><span class="onc-pause-icon"></span>' +
            '<span class="onc-pause-text"></span><span class="onc-pause-count"></span></div>' +
            '<div class="onc-pause-hint"><span class="onc-when-click"></span><span class="onc-when-no-click"></span></div></div>';
        // With the borders shown the window takes clicks: the marker continues too
        div.addEventListener('click', () => { if (div === marker && pause === 'paused') resume(); });
        return div;
    }

    function refreshMarker() {
        if (!marker) return;
        if (pause !== 'paused') {
            marker.querySelector('.onc-pause-text').textContent = 'Pausado às ' + marker.dataset.time;
            marker.querySelector('.onc-pause-count').textContent = '';
            return;
        }
        let count = 0;
        for (let line = marker.nextElementSibling; line; line = line.nextElementSibling)
            if (line.classList.contains('chat_line')) count++;
        marker.querySelector('.onc-pause-text').textContent = 'Chat pausado';
        marker.querySelector('.onc-pause-count').textContent = count ? ' · ' + (pauseLost ? 'mais de ' : '') + messagesText(count) : '';
        const hotkey = settings.pauseHotkey;
        marker.querySelector('.onc-when-click').textContent = hotkey
            ? 'Clique aqui ou aperte ' + hotkey + ' para continuar' : 'Clique aqui para continuar';
        marker.querySelector('.onc-when-no-click').textContent = hotkey
            ? 'Aperte ' + hotkey + ' para continuar' : 'Para continuar, mostre as bordas e clique aqui';
    }

    function tellPause() {
        postToApp('onc:pause-state:' + pause);
    }

    function pauseChat() {
        if (pause === 'paused') return;
        stopReading();
        if (layoutPending) layoutLines(); // what came in the last moment is on screen already
        // The marker goes before the first line not wholly on screen (at the bottom, after the newest one)
        const bottom = box.scrollTop + box.clientHeight;
        let before = null;
        for (const line of box.children) {
            if (line.offsetTop + line.offsetHeight > bottom + 1) { before = line; break; }
        }
        marker = newMarker();
        box.insertBefore(marker, before);
        pause = 'paused';
        pinned = false;
        pauseLost = false;
        newMessages.style.display = 'none';
        readingPill.style.display = 'none';
        refreshMarker();
        // In view at the bottom (measured in its own place, before it can stick)
        box.scrollTop = Math.max(box.scrollTop, marker.offsetTop + marker.offsetHeight - box.clientHeight);
        marker.classList.add('paused');
        tellPause();
    }

    function resume() {
        if (pause !== 'paused') return;
        if (marker) {
            // Nothing came while paused: no divider left behind
            if (!marker.nextElementSibling) {
                marker.remove();
                marker = null;
            } else {
                marker.classList.replace('paused', 'read');
            }
        }
        pause = 'reading';
        refreshMarker();
        if (box.scrollHeight - box.clientHeight - box.scrollTop <= 1) {
            goLive();
            return;
        }
        tellPause();
        nextStep();
    }

    // The newest message reached (or the pill clicked): the chat follows the new messages again
    function goLive() {
        if (pause === 'live') return;
        stopReading();
        if (pause === 'paused' && marker) marker.classList.replace('paused', 'read');
        pause = 'live';
        refreshMarker();
        readingPill.style.display = 'none';
        pin();
        tellPause();
    }

    function stopReading() {
        clearTimeout(readTimer);
        cancelAnimationFrame(readFrame);
        readTimer = readFrame = 0;
        readTarget = null;
    }

    // The next messages not wholly on screen come in (a short slide); then nothing moves while they are read.
    // Messages read faster than STEP_MIN_MS come in together.
    function nextStep() {
        readTimer = 0;
        if (pause !== 'reading') return;
        if (dragging || performance.now() < wheelHoldUntil) {
            readTimer = setTimeout(nextStep, WHEEL_HOLD_MS / 4);
            return;
        }
        const bottom = box.scrollTop + box.clientHeight;
        let first = null;
        for (const line of box.children) {
            if (line.offsetTop + line.offsetHeight > bottom + 1) { first = line; break; }
        }
        if (!first) {
            goLive();
            return;
        }
        const lineHeight = oneLineHeight();
        const catchUp = arrivalSpeed() * CATCH_UP_FACTOR;
        let seconds = 0;
        readToTop = first.offsetHeight > box.clientHeight && first.offsetTop > box.scrollTop + 1;
        if (readToTop) {
            // Taller than the chat: its beginning (the name) first, for the part of it that fits; the rest in the next step
            readTarget = first;
            seconds = readingSeconds(first, lineHeight, catchUp) * box.clientHeight / first.offsetHeight;
        } else {
            for (let line = first; line; line = line.nextElementSibling) {
                // Never so many together that the first one leaves the top of the chat
                if (line !== first && line.offsetTop + line.offsetHeight - Math.max(first.offsetTop, box.scrollTop) > box.clientHeight) break;
                seconds += readingSeconds(line, lineHeight, catchUp);
                readTarget = line;
                if (seconds * 1000 >= STEP_MIN_MS) break;
            }
        }
        readLast = performance.now();
        readSetTo = box.scrollTop;
        cancelAnimationFrame(readFrame);
        readFrame = requestAnimationFrame(slide);
        readTimer = setTimeout(nextStep, seconds * 1000);
        refreshReadingPill();
    }

    // Each frame covers part of the way left, so the slide slows down at the end
    function slide(now) {
        readFrame = 0;
        if (pause !== 'reading' || !readTarget) return;
        const top = box.scrollTop;
        // The person moved the chat (the wheel, the scroll bar): this slide stops, the reading goes on from there
        if (dragging || now < wheelHoldUntil || Math.abs(top - readSetTo) > 1 || !readTarget.isConnected) {
            readTarget = null;
            return;
        }
        // A screen faster than 60 frames a second doesn't draw the page more often
        if (now - readLast < SLIDE_FRAME_MS) {
            readFrame = requestAnimationFrame(slide);
            return;
        }
        const pixel = 1 / (window.devicePixelRatio || 1);
        const target = Math.min(box.scrollHeight - box.clientHeight, readToTop
            ? readTarget.offsetTop
            : readTarget.offsetTop + readTarget.offsetHeight - box.clientHeight);
        const seconds = Math.max(0, (now - readLast) / 1000);
        readLast = now;
        if (target - top <= pixel) {
            if (target > top) box.scrollTop = target;
            readSetTo = box.scrollTop;
            readTarget = null;
            refreshReadingPill();
            return;
        }
        box.scrollTop = top + Math.max(pixel, (target - top) * (1 - Math.exp(-seconds / SLIDE_TIME_S)));
        readSetTo = box.scrollTop;
        readFrame = requestAnimationFrame(slide);
    }

    // How long a message stays to be read: automatic, by its text (between READ_MIN_LINES_PER_SECOND and
    // READ_MAX_LINES_PER_SECOND), or by its height at the fixed speed; never slower than the chat comes
    function readingSeconds(line, lineHeight, catchUp) {
        const height = line.offsetHeight;
        let seconds = settings.resumeSpeed > 0
            ? height / (settings.resumeSpeed * lineHeight)
            : Math.min(Math.max(readingTime(line), height / (READ_MAX_LINES_PER_SECOND * lineHeight)),
                height / (READ_MIN_LINES_PER_SECOND * lineHeight));
        if (catchUp > 0) seconds = Math.min(seconds, height / catchUp);
        return seconds;
    }

    // The height of a message of one line, with its spacing: a "line" of the fixed speed
    function oneLineHeight() {
        const line = box.querySelector('.chat_line');
        if (!line) return 36;
        const style = getComputedStyle(line);
        const height = (parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.25 || 24)
            + (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0);
        return height > 0 ? height : 36;
    }

    function readingTime(line) {
        let time = readingTimes.get(line);
        if (time !== undefined) return time;
        if (!line.classList.contains('chat_line')) {
            time = READ_SECONDS_PER_MESSAGE; // the pause markers
        } else {
            const stamp = line.querySelector('.time_stamp');
            const chars = line.textContent.length - (stamp ? stamp.textContent.length : 0);
            const images = line.querySelectorAll('.message img:not(.onc-gif), .onc-notice img').length;
            const gifs = settings.hideGifs ? 0 : line.querySelectorAll('.onc-gif').length;
            time = READ_SECONDS_PER_MESSAGE + (chars + images * READ_CHARS_PER_IMAGE) / READ_CHARS_PER_SECOND
                + gifs * READ_SECONDS_PER_GIF;
        }
        readingTimes.set(line, time);
        return time;
    }

    // How fast the chat comes, paused or not: the height of the lines added in the last ARRIVALS_MS
    function recordArrivals(added) {
        const now = performance.now();
        while (arrivals.length && arrivals[0][0] < now - ARRIVALS_MS) arrivals.shift();
        if (added.length) arrivals.push([now, added.reduce((sum, line) => sum + line.offsetHeight, 0)]);
    }

    // In CSS pixels a second
    function arrivalSpeed() {
        const now = performance.now();
        while (arrivals.length && arrivals[0][0] < now - ARRIVALS_MS) arrivals.shift();
        return arrivals.reduce((sum, arrival) => sum + arrival[1], 0) / (ARRIVALS_MS / 1000);
    }

    function refreshReadingPill() {
        if (pause !== 'reading') return;
        const bottom = box.scrollTop + box.clientHeight;
        let count = 0;
        // The lines of the step coming into view (up to readTarget) aren't counted
        for (let line = box.lastElementChild; line && line !== readTarget && line.offsetTop >= bottom - 1; line = line.previousElementSibling)
            if (line.classList.contains('chat_line')) count++;
        readingPill.textContent = '↓ ' + messagesText(count);
        readingPill.style.display = count ? 'block' : 'none';
        if (!count) return;
        // At the top, under what is already there: the connection pill (while not faded) and the scroll mode banner
        let top = 6;
        for (const above of [pill, scrollBanner]) {
            if (!above.isConnected || above.classList.contains('faded') || getComputedStyle(above).display === 'none') continue;
            const rect = above.getBoundingClientRect();
            if (rect.height) top = Math.max(top, rect.bottom + 4);
        }
        readingPill.style.top = top + 'px';
    }

    if (window.oncScrollModeWanted) window.oncSetScrollMode(window.oncScrollModeWanted);

    // "Apagar mensagens antigas": a line older than settings.fade seconds fades out (not while paused or reading:
    // the unread messages wait to be read)
    setInterval(() => {
        if (!settings.fade || pause !== 'live') return;
        const limit = Date.now() - settings.fade * 1000;
        for (const line of box.children) {
            if (Number(line.dataset.timestamp) > limit) break;
            if (line.classList.contains('on_out')) continue;
            line.classList.add('on_out');
            line.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 400 }).onfinish = () => line.remove();
        }
    }, 1000);

    // --- AutoMod -------------------------------------------------------------------------------------
    // Messages AutoMod held in the open channel, when the connected account owns or moderates it: the app reads them
    // (EventSub) and keeps them in a queue, oldest first (Twitch/AutoModDoCanal.cs), and sends the state here. A card under
    // the chat shows the oldest one ("1 de 3") with why it was held. With the borders shown (the window takes clicks) its
    // buttons approve or deny it (onc:automod:allow|deny:<id>); without, the hint names the app's hotkeys, which act on
    // that same message. The chat above gets shorter while the card is there, so it never covers the newest message.
    // After a decision the card says how it went for a moment, also with the queue empty.

    const AUTOMOD_NOTICE_MS = 2500;
    const automodCard = document.createElement('div');
    automodCard.id = 'onc-automod';
    automodCard.innerHTML = '<div class="onc-automod-title"><span class="onc-automod-icon"></span>' +
        '<span class="onc-automod-text"></span><span class="onc-automod-count"></span></div>' +
        '<div class="onc-automod-body"><div class="onc-automod-message"></div><div class="onc-automod-actions">' +
        '<button class="onc-automod-allow" title="Aprovar">✓</button><button class="onc-automod-deny" title="Recusar">✕</button>' +
        '</div></div><div class="onc-automod-hint"></div><div class="onc-automod-notice"></div>';
    document.body.append(automodCard);
    let automodState = null;
    let automodNoticeTimer = 0;

    automodCard.querySelector('.onc-automod-allow').addEventListener('click', () => decideAutomod('allow'));
    automodCard.querySelector('.onc-automod-deny').addEventListener('click', () => decideAutomod('deny'));

    function decideAutomod(action) {
        const current = automodState && automodState.current;
        if (current) postToApp('onc:automod:' + action + ':' + current.id);
    }

    // The color the person had in the chat (their messages before this one), or one of Twitch's default colors
    function chatColor(login) {
        const lines = box.querySelectorAll(`.chat_line[data-nick="${CSS.escape(login)}"] .nick`);
        const last = lines[lines.length - 1];
        return last && last.style.color ? last.style.color : userColor(login, '');
    }

    function showAutomod() {
        const state = automodState || {};
        const current = state.current;
        const text = automodCard.querySelector('.onc-automod-text');
        const count = automodCard.querySelector('.onc-automod-count');
        const message = automodCard.querySelector('.onc-automod-message');
        const hint = automodCard.querySelector('.onc-automod-hint');
        automodCard.querySelector('.onc-automod-notice').textContent = state.notice || '';
        automodCard.classList.toggle('waiting', !!current);
        automodCard.classList.toggle('shown', !!(current || state.notice));

        message.textContent = '';
        if (current) {
            text.textContent = 'AutoMod segurou' + (current.reason ? ' · ' + current.reason : '');
            count.textContent = state.total > 1 ? ' · 1 de ' + state.total : '';
            const parts = addOtherEmotes(current.parts.map(part => typeof part === 'string'
                ? part : { emote: twitchEmoteUrls(part.emoteId), name: part.name }));
            message.append(buildLine({ login: current.login, name: current.name, color: chatColor(current.login), parts }));
            // Each hotkey in the color of its button (green approves, red denies)
            hint.textContent = '';
            [[state.allowHotkey, 'aprova', 'allow'], [state.denyHotkey, 'recusa', 'deny']].filter(([key]) => key)
                .forEach(([key, verb, kind], i) => {
                    if (i > 0) hint.append(' · ');
                    const part = document.createElement('span');
                    part.className = 'onc-automod-key-' + kind;
                    const keys = document.createElement('span');
                    keys.className = 'onc-automod-key';
                    keys.textContent = key;
                    part.append(keys, ' ' + verb);
                    hint.append(part);
                });
            if (!hint.firstChild) hint.textContent = 'Mostre as bordas para aprovar ou recusar';
        } else {
            text.textContent = 'AutoMod';
            count.textContent = '';
            hint.textContent = '';
        }
        fitAutomod();
    }

    // The chat ends where the card begins
    function fitAutomod() {
        const shown = automodCard.classList.contains('shown');
        box.style.height = shown ? `calc(100% - ${10 + automodCard.offsetHeight + 6}px)` : '';
        if (pinned && pause === 'live') scrollToBottom();
    }
    new ResizeObserver(fitAutomod).observe(automodCard);

    // --- YouTube (Chat Multiplataforma) -------------------------------------------------------------
    // The app reads the chat of a YouTube live (YouTube/LeitorDoYouTube.cs) and sends each message here
    // (ContratoComAPagina.AdicionarDoYouTube). It goes through the same filters and sound as the Twitch ones: the
    // filter lists hold the YouTube @ (without the @), the owner counts as the broadcaster and moderators as Mods.

    // The name colors YouTube itself uses for the owner, moderators and members
    const YOUTUBE_ROLE_COLORS = { owner: '#FFD600', moderator: '#5E84F1', member: '#2BA640' };
    const YOUTUBE_ROLE_BADGES = { owner: 'broadcaster/1', moderator: 'moderator/1' };

    // YouTube and TikTok have no name colors: one of the default colors from the whole name (the first letter alone
    // gives the same color to everyone named alike, and YouTube names are often alike)
    function colorFromName(login) {
        let hash = 0;
        for (const c of login) hash = (hash * 31 + c.codePointAt(0)) >>> 0;
        return DEFAULT_COLORS[hash % DEFAULT_COLORS.length];
    }

    function onYouTubeMessage(m) {
        const login = String(m.login || m.name || '').toLowerCase();
        const text = (m.parts || []).filter(part => typeof part === 'string').join('').trim();
        if (m.notice && !settings.youTubeNotices) return;
        // A bot by its name: YouTube marks no bots (see "Bots")
        if (settings.hideBots && !m.superchat && !m.notice && (text[0] === '!' || /bot$/.test(login))) return;

        showLine({
            id: 'yt-' + m.id,
            platform: 'youtube',
            login,
            name: m.name || login,
            color: userColor(login || '?', YOUTUBE_ROLE_COLORS[m.role] || colorFromName(login)),
            action: false,
            parts: m.parts || [],
            superchat: m.superchat || null,
            notice: m.notice || ''
        }, YOUTUBE_ROLE_BADGES[m.role] || '', '');
    }

    // --- TikTok (Chat Multiplataforma) --------------------------------------------------------------
    // The app reads the chat of a TikTok live (TikTok/LeitorDoTikTok.cs) and sends each message here
    // (ContratoComAPagina.AdicionarDoTikTok), like the YouTube ones: same filters and sound, the filter lists hold the
    // TikTok @ (without the @), the owner counts as the broadcaster and moderators as Mods. TikTok has no name colors.

    const TIKTOK_ROLE_BADGES = { owner: 'broadcaster/1', moderator: 'moderator/1' };
    // The switch of each kind of TikTok notice, in the settings
    const TIKTOK_NOTICES = { gift: 'tiktokGifts', follow: 'tiktokFollows', share: 'tiktokShares', superfan: 'tiktokSuperFans' };

    function onTikTokMessage(m) {
        const login = String(m.login || m.name || '').toLowerCase();
        const text = (m.parts || []).filter(part => typeof part === 'string').join('').trim();
        // A notice (gift, follow, share, Super Fan) has its own switch each (TIKTOK_NOTICES)
        if (m.kind && !settings[TIKTOK_NOTICES[m.kind]]) return;
        // A bot by its name: TikTok marks no bots (see "Bots")
        if (settings.hideBots && !m.kind && (text[0] === '!' || /bot$/.test(login))) return;

        showLine({
            id: 'tt-' + m.id,
            platform: 'tiktok',
            kind: m.kind || '',
            login,
            name: m.name || login,
            color: userColor(login || '?', colorFromName(login)),
            action: false,
            parts: m.parts || [],
            notice: m.notice || ''
        }, TIKTOK_ROLE_BADGES[m.role] || '', '');
    }

    // --- Kick (Chat Multiplataforma) ----------------------------------------------------------------
    // Read here, like the Twitch IRC: the channel's chat room id comes from Kick's site API (it answers this page),
    // then the Pusher WebSocket the site itself uses sends the room's messages. Read-only, no account. The same
    // filters and sound as the Twitch ones: the filter lists hold the Kick name, moderators count as Mods.
    // The app shows the state in the channel strip (onc:kick-state, see LogicaFaixaDoCanal).

    const KICK_API = 'https://kick.com/api/v2/channels/';
    // Kick's Pusher key and cluster. The site gets them from web.kick.com/api/v1/realtime/channels/{id}/chat/connection
    // (asking for "pusher" only), but that API doesn't answer this page (CORS), so they're written here. When Kick
    // changes them, the connection is refused with "pusher:error" (in the DevTools console) and the strip shows red.
    const KICK_PUSHER_URL = 'wss://ws-us2.pusher.com/app/34bf7a0ff419a2a775b9?protocol=7&client=js&version=8.4.0&flash=false';
    const KICK_ACTIVITY_MS = 120000;   // Pusher's activity timeout: a ping when nothing came for this long...
    const KICK_PONG_LIMIT_MS = 30000;  // ...and a new connection when even the pong doesn't come
    const KICK_API_RETRY_MS = 20000;   // the site API didn't answer: try again
    const KICK_BADGES = { broadcaster: 'broadcaster/1', moderator: 'moderator/1', vip: 'vip/1' };

    const kick = {
        channel: '',       // the channel being read ('' = none)
        room: null,        // its chat room id
        subBadges: [],     // the channel's subscriber badges, [{ months, src }] from the fewest months
        socket: null,
        lastData: 0,
        pingSent: false,
        retryDelay: RETRY_MIN_MS,
        retryTimer: null,
        state: ''
    };

    // The Kick channel in the settings, read only with the Chat Multiplataforma on
    function updateKick() {
        const wanted = started && settings.multiplatform ? String(settings.kick || '') : '';
        if (wanted === kick.channel) return;
        stopKick();
        kick.channel = wanted;
        if (wanted) loadKickChannel(wanted);
        else setKickState('off');
    }

    function stopKick() {
        clearTimeout(kick.retryTimer);
        const old = kick.socket;
        kick.socket = null;
        kick.room = null;
        kick.subBadges = [];
        kick.retryDelay = RETRY_MIN_MS;
        if (old) old.close();
    }

    function setKickState(newState) {
        if (newState === kick.state) return;
        kick.state = newState;
        postToApp('onc:kick-state:' + newState);
        setPlatformState('kick', newState === 'disconnected' ? 'failed' : newState);
    }

    // The Chat Multiplataforma was switched on or off while the chat is open: the pill tells the new way
    function refreshPill() {
        if (settings.multiplatform) {
            if (state) platformStates.twitch = state === 'unavailable' ? 'notfound' : state === 'disconnected' ? 'failed' : state;
            multiPillText = '';
            showMultiPill();
        } else if (channel && state) {
            showPill(state, stateTexts[state], state === 'connected' ? 4000 : 0);
        } else {
            pill.remove();
        }
    }

    function loadKickChannel(name) {
        setKickState('connecting');
        fetch(KICK_API + encodeURIComponent(name))
            .then(response => {
                if (kick.channel !== name) return;
                if (response.status === 404) {
                    setKickState('notfound');
                    return;
                }
                if (!response.ok) throw new Error('HTTP ' + response.status);
                return response.json().then(data => {
                    if (kick.channel !== name) return;
                    kick.room = data.chatroom.id;
                    kick.subBadges = (data.subscriber_badges || [])
                        .filter(b => b.badge_image && b.badge_image.src)
                        .map(b => ({ months: Number(b.months) || 0, src: b.badge_image.src }))
                        .sort((a, b) => a.months - b.months);
                    connectKick();
                });
            })
            .catch(e => {
                if (kick.channel !== name) return;
                console.warn('[ONC] Could not find the Kick channel ' + name, e);
                setKickState('disconnected');
                kick.retryTimer = setTimeout(() => loadKickChannel(name), KICK_API_RETRY_MS);
            });
    }

    function connectKick() {
        clearTimeout(kick.retryTimer);
        setKickState('connecting');
        const ws = new WebSocket(KICK_PUSHER_URL);
        kick.socket = ws;
        kick.lastData = Date.now();
        kick.pingSent = false;

        const send = (event, data) => ws.send(JSON.stringify({ event, data }));
        ws.onmessage = (message) => {
            if (ws !== kick.socket) return;
            kick.lastData = Date.now();
            kick.pingSent = false;
            let event, data;
            try {
                event = JSON.parse(message.data);
                data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
            } catch (e) {
                console.error('[ONC] Could not read the Kick event: ' + message.data, e);
                return;
            }
            switch (event.event) {
                case 'pusher:connection_established':
                    send('pusher:subscribe', { auth: '', channel: 'chatrooms.' + kick.room + '.v2' });
                    break;
                case 'pusher_internal:subscription_succeeded':
                    kick.retryDelay = RETRY_MIN_MS;
                    setKickState('connected');
                    break;
                case 'pusher:ping':
                    send('pusher:pong', {});
                    break;
                case 'pusher:error':
                    console.warn('[ONC] Kick refused the connection: ' + message.data);
                    break;
                case 'App\\Events\\ChatMessageEvent':
                    onKickMessage(data);
                    break;
                case 'App\\Events\\MessageDeletedEvent':
                    if (data && data.message && data.message.id)
                        removeLines(`.chat_line[data-id="${CSS.escape('kick-' + data.message.id)}"]`);
                    break;
                case 'App\\Events\\UserBannedEvent':
                    if (data && data.user && data.user.username)
                        removeLines(`.chat_line[data-id^="kick-"][data-nick="${CSS.escape(String(data.user.username).toLowerCase())}"]`);
                    break;
                case 'App\\Events\\ChatroomClearEvent':
                    removeLines('.chat_line[data-id^="kick-"]');
                    break;
            }
        };
        ws.onclose = () => {
            if (ws !== kick.socket) return;
            kick.socket = null;
            setKickState('disconnected');
            kick.retryTimer = setTimeout(connectKick, kick.retryDelay);
            kick.retryDelay = Math.min(kick.retryDelay * 2, RETRY_MAX_MS);
        };
    }

    // Kick's chat can stay quiet for minutes: a ping keeps the connection, one that stopped answering is replaced
    setInterval(() => {
        const ws = kick.socket;
        if (!ws || ws.readyState !== WebSocket.OPEN) return;
        const silentFor = Date.now() - kick.lastData;
        if (kick.pingSent && silentFor > KICK_ACTIVITY_MS + KICK_PONG_LIMIT_MS) {
            ws.close();
        } else if (!kick.pingSent && silentFor > KICK_ACTIVITY_MS) {
            kick.pingSent = true;
            ws.send(JSON.stringify({ event: 'pusher:ping', data: {} }));
        }
    }, 5000);

    // "[emote:37226:KEKW]" in the text is a Kick emote
    function kickParts(text) {
        const parts = [];
        let last = 0;
        for (const m of text.matchAll(/\[emote:(\d+):([^\]]*)\]/g)) {
            if (m.index > last) parts.push(text.slice(last, m.index));
            parts.push({ emote: { src: 'https://files.kick.com/emotes/' + m[1] + '/fullsize' }, name: m[2] });
            last = m.index + m[0].length;
        }
        if (last < text.length) parts.push(text.slice(last));
        return parts;
    }

    // The channel's subscriber badge for that many months (the one of the most months reached, at least the first)
    function kickSubBadge(months) {
        let badge = null;
        kick.subBadges.forEach(b => { if (b.months <= months || !badge) badge = b; });
        if (!badge) return null;
        const img = document.createElement('img');
        img.src = badge.src;
        return img;
    }

    function onKickMessage(m) {
        const sender = (m && m.sender) || {};
        const identity = sender.identity || {};
        const login = String(sender.username || sender.slug || '').toLowerCase();
        const text = String(m.content || '');
        if (!login || !text) return;
        // A bot by its name: Kick marks no bots (see "Bots")
        if (settings.hideBots && (text[0] === '!' || /bot$/.test(login))) return;

        // Broadcaster, Mods and VIPs count for the filters; the subscriber badge is the channel's own picture
        const badges = [], shown = [];
        (identity.badges || []).forEach(b => {
            if (KICK_BADGES[b.type]) {
                badges.push(KICK_BADGES[b.type]);
                shown.push(KICK_BADGES[b.type]);
            } else if (b.type === 'subscriber') {
                shown.push('kicksub/' + (Number(b.count) || 0));
            }
        });

        showLine({
            id: 'kick-' + m.id,
            platform: 'kick',
            login,
            name: sender.username || login,
            color: userColor(login, identity.color),
            action: false,
            parts: kickParts(text)
        }, badges.join(','), shown.join(','));
    }

    // The platform icons only while the Chat Multiplataforma is on (also on the lines already shown)
    function showPlatforms() {
        document.body.classList.toggle('onc-multi', !!settings.multiplatform);
    }

    // --- Used by the app -----------------------------------------------------------------------------

    window.oncChat = {
        start(newSettings) {
            Object.assign(settings, newSettings);
            showOrHideGifs();
            showPlatforms();
            if (started) return;
            started = true;
            if (channel) connect();
            updateKick();
        },

        // Settings saved while the chat is open (see MainWindow.LiveSettings.cs)
        apply(newSettings) {
            Object.assign(settings, newSettings);
            showOrHideGifs();
            showPlatforms();
            if (settings.hideOtherChannels) removeLines('.onc-other-channel');
            // A notice switched off goes away from the screen too
            if (!settings.twitchNotices) removeLines('.onc-notice-twitch');
            if (!settings.youTubeNotices) removeLines('.onc-notice-youtube');
            Object.entries(TIKTOK_NOTICES).forEach(([kind, option]) => {
                if (!settings[option]) removeLines('.onc-tiktok-' + kind);
            });
            if (!settings.multiplatform) removeLines('.chat_line[data-id^="yt-"], .chat_line[data-id^="kick-"], .chat_line[data-id^="tt-"]');
            const wasMultiplatform = pill.classList.contains('multi');
            updateKick();
            if (wasMultiplatform !== !!settings.multiplatform) refreshPill();
            refreshMarker(); // the pause hotkey may be another one
        },

        // The app's pause button and hotkey: pauses, or continues a paused chat (see "Pause"). The new state:
        // 'paused', 'reading' or 'live' (also sent to the app as onc:pause-state:...)
        togglePause() {
            if (pause === 'paused') resume();
            else pauseChat();
            return pause;
        },

        // Chat Multiplataforma: how the YouTube or the TikTok reading is (the app reads them): connecting, connected,
        // waiting, failed, notfound or off
        platformState(platform, newState) {
            setPlatformState(platform, newState);
        },

        // A message of the YouTube chat (Chat Multiplataforma)
        addYouTube(message) {
            if (!started || !settings.multiplatform) return;
            onYouTubeMessage(message);
        },

        // A message of the TikTok chat (Chat Multiplataforma)
        addTikTok(message) {
            if (!started || !settings.multiplatform) return;
            onTikTokMessage(message);
        },

        // A TikTok moderator deleted messages: by their ids and by the @ of who sent them
        removeTikTok(ids, logins) {
            (ids || []).forEach(id => removeLines(`.chat_line[data-id="${CSS.escape('tt-' + id)}"]`));
            (logins || []).forEach(login => removeLines(`.chat_line[data-id^="tt-"][data-nick="${CSS.escape(String(login).toLowerCase())}"]`));
        },

        // A line from the app in the user's color, like a /me message (channel point redemptions). The same text
        // already shown as a chat message is taken out (see "Redemptions").
        addAction(name, color, text) {
            const login = name.toLowerCase();
            const reward = { by: login, text: text.trim() };
            const chatMessage = unpairedRewardLine(reward, false);
            if (chatMessage) {
                chatMessage.remove();
                reward.paired = true;
            }
            showLine({ login, name, color: userColor(login, color), action: true, redemption: true, reward, parts: addOtherEmotes([text]) }, '');
        },

        // The AutoMod queue (see "AutoMod"): { current: { id, login, name, parts, reason } or null, total, allowHotkey,
        // denyHotkey, notice }. The notice (how the last decision went) shows for a moment.
        automod(state) {
            automodState = state;
            clearTimeout(automodNoticeTimer);
            if (state && state.notice) {
                automodNoticeTimer = setTimeout(() => {
                    if (automodState === state) {
                        automodState = Object.assign({}, state, { notice: '' });
                        showAutomod();
                    }
                }, AUTOMOD_NOTICE_MS);
            }
            showAutomod();
        },

        // For the app's watchdog: "open:<ms since Twitch last sent something>" or the state
        health() {
            if (!started) return 'missing';
            if (!channel) return 'open:0'; // without Twitch there is nothing to watch
            if (socket && socket.readyState === WebSocket.OPEN) return 'open:' + (Date.now() - lastData);
            return 'disconnected';
        }
    };
})();
