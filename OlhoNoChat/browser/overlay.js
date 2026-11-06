// The overlay for the live: an OBS Browser source at http://localhost:<port>/overlay, served by the app
// (Overlay/ServidorDoOverlay.cs). It shows only chat messages: the lines the "Padrão" chat showed, already past its
// filters (chat.js sends each one to the app), never its AutoMod card, pause marker, banners or status pills.
// Events (Server-Sent Events from /overlay/eventos): "config" (the options of the Overlay tab, OverlayContract
// JsonDasOpcoes), "line" (a line's HTML), "remove" (a CSS selector of lines that left the chat: a deleted message, a
// ban), "replace" (a line that changed: its badges arrived) and "clear". Real time only (the user's choice): nothing
// comes back after a reconnection, the overlay shows what arrives from then on.
(function () {
    'use strict';

    const box = document.getElementById('ov-lines');
    const LEAVE_MS = 500;
    // The "Animação" options (overlay.css has a way in and a way out for each)
    const ANIMATIONS = ['slide', 'fade', 'rise', 'pop', 'bounce', 'blur', 'flip'];
    // Twitch notices that are subs (chat.js puts the kind in the class: onc-twitch-<kind>)
    const SUB_KINDS = ['sub', 'resub', 'subgift', 'submysterygift', 'giftpaidupgrade', 'anongiftpaidupgrade',
        'primepaidupgrade', 'standardpayforward', 'communitypayforward'];
    // Combining marks kept per character (piled-up marks, "zalgo", cover the lines above)
    const MAX_MARKS = 4;

    let options = null;
    let pageVersion = null;
    let direction = null;
    let bannedWords = null;
    // Lines waiting for the delay: { wrapper, timer }
    let waiting = [];

    // --- Options ----------------------------------------------------------------------------------------

    function apply(newOptions) {
        options = newOptions;
        const root = document.documentElement.style;
        root.setProperty('--ov-size', options.fontSize + 'px');
        root.setProperty('--ov-font', '"' + String(options.font).replace(/"/g, '') + '"');
        root.setProperty('--ov-weight', options.bold ? 'bold' : 'normal');
        root.setProperty('--ov-card-opacity', String(options.backgroundOpacity));
        const width = Math.max(1, Math.round(options.fontSize / 14));
        root.setProperty('--ov-shadow', {
            theme: `-${width}px -${width}px 0 #000, ${width}px -${width}px 0 #000, -${width}px ${width}px 0 #000, ${width}px ${width}px 0 #000`,
            soft: '0 1px 4px rgba(0, 0, 0, .9), 0 0 2px rgba(0, 0, 0, .8)'
        }[options.outline] || 'none');

        const body = document.body.classList;
        body.toggle('ov-top', options.direction === 'top');
        body.toggle('ov-right', options.align === 'right');
        body.toggle('ov-card', options.background === 'card');
        body.toggle('ov-time', !!options.time);
        body.toggle('ov-badges', !!options.badges);
        body.toggle('onc-multi', !!options.platform); // chat.css shows the platform icons with it
        body.toggle('onc-hide-gifs', !!options.hideGifs); // the chat's "Esconder GIFs" (chat.css shows the title)

        // The lines on the screen are in the order they show: another direction turns them around
        if (direction && direction !== options.direction) box.replaceChildren(...lines().reverse());
        direction = options.direction;

        bannedWords = bannedWordsPattern(options.bannedWords || []);
        trim();
        // A new fade time counts from when each line came in
        lines().forEach(scheduleLeave);
    }

    // Whole words, any case; a * at the end also takes what comes after ("idiot*" takes "idiota" and "idiotas")
    function bannedWordsPattern(words) {
        const parts = words.map(word => String(word).trim()).filter(Boolean).map(word => {
            const open = word.endsWith('*');
            const escaped = word.replace(/\*+$/, '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            return escaped ? escaped + (open ? '[\\p{L}\\p{N}]*' : '') : '';
        }).filter(Boolean);
        return parts.length ? new RegExp('(?<![\\p{L}\\p{N}])(?:' + parts.join('|') + ')(?![\\p{L}\\p{N}])', 'giu') : null;
    }

    // --- A line from the chat -----------------------------------------------------------------------------

    const template = document.createElement('template');

    // The chat's line, ready for the live, or null when an option of the overlay leaves it out
    function prepare(html) {
        template.innerHTML = html;
        const line = template.content.firstElementChild;
        if (!line || !line.classList.contains('chat_line')) return null;
        clean(line);

        if (!wantedNotice(line)) return null;
        if (options.hideBots && line.hasAttribute('data-bot')) return null;
        const message = line.querySelector('.message');
        if (message && !isNotice(line)) {
            const prefix = String(options.commandPrefix || '');
            if (options.hideCommands && prefix && messageText(message).trimStart().startsWith(prefix)) return null;
        }
        fixTexts(line);
        if (message) {
            const links = message.querySelectorAll('a.onc-link');
            if (links.length && options.links === 'hide') return null;
            if (options.links !== 'show') links.forEach(link => link.replaceWith('[link]'));
            if (options.maxEmotes > 0) limitEmotes(message, options.maxEmotes);
            if (options.maxChars > 0) limitText(message, options.maxChars);
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'ov-line';
        wrapper.append(line);
        return wrapper;
    }

    // The HTML comes from the chat page, built with text nodes only; still, nothing that runs or loads a page stays
    function clean(root) {
        root.querySelectorAll('script, style, link, iframe, object, embed, frame, base, meta, form, input, button, textarea, svg, math')
            .forEach(element => element.remove());
        [root, ...root.querySelectorAll('*')].forEach(element => {
            [...element.attributes].forEach(attribute => {
                const name = attribute.name.toLowerCase();
                if (name.startsWith('on') || (name === 'href' && !/^https?:/i.test(attribute.value)))
                    element.removeAttribute(attribute.name);
            });
        });
    }

    function isNotice(line) {
        return line.classList.contains('onc-notice-line') || line.classList.contains('onc-redemption');
    }

    // The switches of "Avisos na live"
    function wantedNotice(line) {
        const has = name => line.classList.contains(name);
        const notices = options.notices || {};
        if (has('onc-redemption')) return notices.redemptions !== false;
        if (has('onc-superchat')) return notices.superchats !== false;
        if (has('onc-notice-youtube')) return notices.youtubeMembers !== false;
        if (has('onc-tiktok-gift')) return notices.tiktokGifts !== false;
        if (has('onc-notice-tiktok')) return notices.tiktokOther !== false;
        if (has('onc-notice-twitch')) {
            if (has('onc-twitch-raid')) return notices.raids !== false;
            if (SUB_KINDS.some(kind => has('onc-twitch-' + kind))) return notices.subs !== false;
            return notices.twitchOther !== false;
        }
        return true;
    }

    // What the viewer wrote (a Super Chat's amount comes first in the message: it doesn't count)
    function messageText(message) {
        const copy = message.cloneNode(true);
        copy.querySelectorAll('.onc-superchat-amount').forEach(amount => amount.remove());
        return copy.textContent;
    }

    // The texts shown: not the time, a Super Chat's amount, nor the GIF title chat.css hides while the GIF shows
    function textNodes(root) {
        const skip = '.time_stamp, .onc-superchat-amount' + (document.body.classList.contains('onc-hide-gifs') ? '' : ', .onc-gif-title');
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const nodes = [];
        while (walker.nextNode()) {
            if (!walker.currentNode.parentElement.closest(skip)) nodes.push(walker.currentNode);
        }
        return nodes;
    }

    // Banned words become ****, and piled-up combining marks go
    function fixTexts(line) {
        const marks = new RegExp('(\\p{M}{' + MAX_MARKS + '})\\p{M}+', 'gu');
        textNodes(line).forEach(node => {
            let text = node.nodeValue.replace(marks, '$1');
            if (bannedWords) text = text.replace(bannedWords, '****');
            if (text !== node.nodeValue) node.nodeValue = text;
        });
    }

    function limitEmotes(message, max) {
        // An emote over another one (a hat) goes with it
        const emotes = [...message.querySelectorAll('img.emoticon:not(.onc-overlay)')];
        emotes.slice(max).forEach(img => (img.closest('.onc-stack') || img).remove());
    }

    // Counted in characters (an emoji is one); the rest of the message goes, with "…" at the cut
    function limitText(message, max) {
        let left = max;
        for (const node of textNodes(message)) {
            const chars = [...node.nodeValue];
            if (chars.length <= left) {
                left -= chars.length;
                continue;
            }
            node.nodeValue = chars.slice(0, left).join('').trimEnd() + '…';
            const rest = document.createRange();
            rest.setStartAfter(node);
            rest.setEndAfter(message.lastChild);
            rest.deleteContents();
            return;
        }
    }

    // --- On the screen ------------------------------------------------------------------------------------

    function lines() {
        return [...box.children];
    }

    function show(wrapper) {
        wrapper.shownAt = performance.now();
        if (ANIMATIONS.includes(options.animation))
            wrapper.classList.add('ov-enter-' + options.animation);
        if (options.direction === 'top') box.prepend(wrapper);
        else box.append(wrapper);
        trim();
        scheduleLeave(wrapper);
    }

    // The oldest lines go when there are more than the maximum
    function trim() {
        const all = lines().filter(line => !line.classList.contains('ov-leaving'));
        const extra = all.length - options.maxLines;
        if (extra <= 0) return;
        const oldest = options.direction === 'top' ? all.slice(-extra) : all.slice(0, extra);
        oldest.forEach(remove);
    }

    function scheduleLeave(wrapper) {
        clearTimeout(wrapper.leaveTimer);
        if (!options.fadeSeconds || wrapper.classList.contains('ov-leaving')) return;
        const left = options.fadeSeconds * 1000 - (performance.now() - wrapper.shownAt);
        wrapper.leaveTimer = setTimeout(() => leave(wrapper), Math.max(0, left));
    }

    function leave(wrapper) {
        if (!wrapper.isConnected || wrapper.classList.contains('ov-leaving')) return;
        if (!ANIMATIONS.includes(options.animation)) {
            remove(wrapper);
            return;
        }
        // The way in is over: the way out takes its place
        wrapper.classList.remove(...ANIMATIONS.map(name => 'ov-enter-' + name));
        wrapper.classList.add('ov-leaving', 'ov-leave-' + options.animation);
        setTimeout(() => remove(wrapper), LEAVE_MS);
    }

    function remove(wrapper) {
        clearTimeout(wrapper.leaveTimer);
        wrapper.remove();
    }

    // --- Events -------------------------------------------------------------------------------------------

    function onLine(data) {
        if (typeof data.html !== 'string') return;
        const wrapper = prepare(data.html);
        if (!wrapper) return;

        const delay = data.test ? 0 : options.delaySeconds * 1000;
        if (delay <= 0) {
            show(wrapper);
            return;
        }
        const item = { wrapper };
        item.timer = setTimeout(() => {
            waiting = waiting.filter(other => other !== item);
            show(wrapper);
        }, delay);
        waiting.push(item);
    }

    function onRemove(data) {
        if (typeof data.selector !== 'string') return;
        try {
            box.querySelectorAll(data.selector).forEach(found => {
                const wrapper = found.closest('.ov-line');
                if (wrapper) remove(wrapper);
            });
            // Still waiting for the delay: it never shows (the reason for the delay)
            waiting = waiting.filter(item => {
                if (!item.wrapper.querySelector(data.selector)) return true;
                clearTimeout(item.timer);
                return false;
            });
        } catch (e) {
            // Not a selector this browser knows
        }
    }

    // A line that changed: swapped where it is (on the screen or still waiting), or out if an option now leaves it out
    function onReplace(data) {
        if (typeof data.selector !== 'string' || typeof data.html !== 'string') return;
        try {
            const found = [...box.querySelectorAll(data.selector)].map(line => line.closest('.ov-line'))
                .concat(waiting.map(item => item.wrapper).filter(wrapper => wrapper.querySelector(data.selector)));
            if (!found.length) return;
            const fresh = prepare(data.html);
            found.forEach(wrapper => {
                if (!wrapper) return;
                if (fresh) wrapper.replaceChildren(fresh.firstElementChild.cloneNode(true));
                else dropEverywhere(wrapper);
            });
        } catch (e) {
            // Not a selector this browser knows
        }
    }

    function dropEverywhere(wrapper) {
        waiting = waiting.filter(item => {
            if (item.wrapper !== wrapper) return true;
            clearTimeout(item.timer);
            return false;
        });
        remove(wrapper);
    }

    function clearAll() {
        waiting.forEach(item => clearTimeout(item.timer));
        waiting = [];
        lines().forEach(remove);
    }

    function onClear() {
        clearAll();
    }

    function onConfig(data) {
        apply(data);
    }

    // The app came back with other files for this page (an update): OBS keeps the old page loaded, so it loads again
    function onVersion(data) {
        if (typeof data.v !== 'string') return;
        if (pageVersion && pageVersion !== data.v) location.reload();
        pageVersion = data.v;
    }

    function read(handler) {
        return event => {
            let data;
            try {
                data = JSON.parse(event.data);
            } catch (e) {
                return;
            }
            if (data && typeof data === 'object' && (options || handler === onConfig || handler === onVersion)) handler(data);
        };
    }

    // EventSource connects again by itself (the app closed, OBS woke up): every 2 s, as the app asks ("retry"). An answer
    // that isn't the app's (another program took the port) closes it for good: then a new one, a little later.
    // Without the app nothing on the live can be deleted any more: what was there goes, and what waited never shows (and
    // connecting again starts empty: real time only).
    function connect() {
        const events = new EventSource('/overlay/eventos');
        events.addEventListener('version', read(onVersion));
        events.addEventListener('config', read(onConfig));
        events.addEventListener('line', read(onLine));
        events.addEventListener('remove', read(onRemove));
        events.addEventListener('replace', read(onReplace));
        events.addEventListener('clear', read(onClear));
        events.onerror = () => {
            clearAll();
            if (events.readyState === EventSource.CLOSED) setTimeout(connect, 5000);
        };
    }

    connect();
})();
