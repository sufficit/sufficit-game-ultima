// Ultima — cliente do servidor autoritativo (Marco 3, F4):
// sessão via API, pose do jogador local, avatares remotos vindos do servidor
// (incluindo personagens de agentes), chat global e mapa editável (tiles + jobs).
// Independente do P2P (net.js): os dois convivem — P2P dá voz/áudio, a API dá o mundo.
(function () {
    'use strict';

    const FALLBACK_API = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';
    const qs = new URLSearchParams(location.search);
    const API = (qs.get('api') || window.ULTIMA_API || FALLBACK_API).replace(/\/+$/, '');
    const enabled = qs.get('live') !== '0';
    const POSE_MS = 400;          // cadência de pose para o servidor (2,5/s; limite: 80 cmd/10s)
    const POSE_JUMPM = 2.0;       // moveu muito → envia na hora
    const MAP_REFRESH_MS = 1000;  // debounce de /v1/game/map

    let dotnet = null;
    let token = null;
    let myId = null;
    let since = 0;
    let stopped = false;
    let joined = false;
    let lastPoseAt = 0;
    let lastSent = null;          // {x, z, ry} último enviado
    let lastMapAt = 0;
    let mapTimer = 0;
    let stats = { tiles: 0, jobs: 0 };

    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    async function call(method, path, body, timeoutMs, keepalive) {
        const ctl = new AbortController();
        const timer = setTimeout(() => ctl.abort(), timeoutMs || 25000);
        try {
            const headers = {};
            if (body !== undefined) { headers['Content-Type'] = 'application/json'; }
            if (token) { headers['X-Game-Token'] = token; }
            const res = await fetch(API + path, {
                method: method,
                headers: headers,
                body: body === undefined ? undefined : JSON.stringify(body),
                cache: 'no-store',
                signal: ctl.signal,
                keepalive: !!keepalive
            });
            const text = await res.text();
            let data = null;
            try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
            if (!res.ok) {
                const err = new Error('http ' + res.status);
                err.status = res.status;
                err.data = data;
                throw err;
            }
            return data;
        } finally {
            clearTimeout(timer);
        }
    }

    // ---------- mundo local (avatares + mapa) ----------
    function paintCharacters(list) {
        if (!window.game || !Array.isArray(list)) { return; }
        for (const ch of list) {
            if (!ch || ch.id === myId) { continue; }
            game.addRemote(ch.id, ch.name);           // no-op se já existe (P2P ou servidor)
            game.renameRemote(ch.id, ch.name);
            game.updateRemote(ch.id, Number(ch.x) || 0, Number(ch.z) || 0, Number(ch.yaw) || 0);
        }
    }

    function refreshMap() {
        const now = Date.now();
        if (now - lastMapAt < MAP_REFRESH_MS) {
            clearTimeout(mapTimer);
            mapTimer = setTimeout(refreshMap, MAP_REFRESH_MS - (now - lastMapAt) + 50);
            return;
        }
        lastMapAt = now;
        call('GET', '/v1/game/map', undefined, 10000).then((m) => {
            if (!m || stopped) { return; }
            stats.tiles = Object.keys(m.tiles || {}).length;
            stats.jobs = (m.jobs || []).length;
            if (window.game) {
                if (game.setMapTiles) { game.setMapTiles(m.tiles || {}); }
                if (game.setMapJobs) { game.setMapJobs(m.jobs || []); }
            }
        }).catch(() => { /* mapa volta no próximo evento/snapshot */ });
    }

    function handleEvent(ev) {
        if (!ev || stopped) { return; }
        const kind = ev.type;
        if (kind === 'snapshot') {
            paintCharacters(ev.characters);
            refreshMap();
        } else if (kind === 'join') {
            if (ev.character && ev.character.id !== myId && window.game) {
                game.addRemote(ev.character.id, ev.character.name);
            }
        } else if (kind === 'leave') {
            if (ev.character && window.game) { game.removeRemote(ev.character.id); }
        } else if (kind === 'say') {
            if (ev.from !== myId && dotnet) { dotnet.invokeMethodAsync('OnChat', ev.name || 'Jogador', ev.text || ''); }
        } else if (kind === 'npc') {
            if (dotnet && (!ev.to || ev.to === myId)) { dotnet.invokeMethodAsync('OnChat', ev.name || 'Guardiã', ev.text || ''); }
        } else if (kind === 'job_started' || kind === 'job_done' || kind === 'job_cancelled') {
            refreshMap();
        }
    }

    async function eventLoop() {
        while (!stopped && token) {
            try {
                const res = await call('GET', '/v1/game/events?since=' + since + '&wait=15', undefined, 22000);
                for (const ev of (res && res.events) || []) {
                    handleEvent(ev);
                    since = Math.max(since, ev.seq || 0);
                }
            } catch (e) {
                if (e && e.status === 401) { await rejoin(); continue; }
                await sleep(3000);
            }
        }
    }

    // ---------- sessão ----------
    async function resume() {
        if (!token) { return false; }
        try {
            const st = await call('GET', '/v1/game/state', undefined, 8000);
            if (!st || !st.me) { return false; }
            myId = st.me.id;
            since = st.eventsFrom || 0;
            paintCharacters(st.characters);
            stats.tiles = Object.keys((st.map && st.map.tiles) || {}).length;
            stats.jobs = ((st.map && st.map.jobs) || []).length;
            if (window.game) {
                if (game.setMapTiles) { game.setMapTiles((st.map && st.map.tiles) || {}); }
                if (game.setMapJobs) { game.setMapJobs((st.map && st.map.jobs) || []); }
            }
            return true;
        } catch (e) {
            token = null;
            try { sessionStorage.removeItem('ultima-live-token'); } catch (e2) { /* sem storage */ }
            return false;
        }
    }

    async function join() {
        let name = null;
        try { name = (window.game && game.net && game.net.getName) ? game.net.getName() : null; } catch (e) { /* net.js ausente */ }
        for (let attempt = 0; attempt < 5 && !stopped; attempt++) {
            try {
                const res = await call('POST', '/v1/game/sessions', { name: name || 'Jogador' }, 10000);
                token = res.token;
                myId = res.character && res.character.id;
                since = res.eventsFrom || 0;
                joined = true;
                try { sessionStorage.setItem('ultima-live-token', token); } catch (e) { /* sem storage */ }
                paintCharacters(null);
                refreshMap();
                eventLoop();
                return true;
            } catch (e) {
                await sleep(1500 * (attempt + 1));
            }
        }
        if (dotnet) { dotnet.invokeMethodAsync('OnNetStatus', 'Servidor do jogo inacessível', 0); }
        return false;
    }

    async function rejoin() {
        token = null;
        myId = null;
        joined = false;
        try { sessionStorage.removeItem('ultima-live-token'); } catch (e) { /* sem storage */ }
        await join();
    }

    // ---------- comandos do jogador local ----------
    function pose(x, z, ry) {
        if (!token || stopped || !Number.isFinite(x) || !Number.isFinite(z)) { return; }
        const now = Date.now();
        const bigMove = !lastSent || Math.abs(x - lastSent.x) + Math.abs(z - lastSent.z) > POSE_JUMPM;
        if (now - lastPoseAt < POSE_MS && lastSent && !bigMove) { return; }
        lastPoseAt = now;
        lastSent = { x: x, z: z, ry: ry };
        call('POST', '/v1/game/command', { action: 'pose', x: x, z: z, yaw: ry }, 8000).catch((e) => {
            if (e && e.status === 401) { rejoin(); }
        });
    }

    function sendChat(text) {
        const clean = String(text || '').slice(0, 300);
        if (!clean || !token || stopped) { return; }
        call('POST', '/v1/game/command', { action: 'say', text: clean }, 10000).catch((e) => {
            if (e && e.status === 401) { rejoin(); }
        });
    }

    function info() {
        return { api: API, enabled: enabled, connected: joined && !!token, id: myId, since: since, stats: stats };
    }

    window.game.live = {
        init(dotnetRef) {
            dotnet = dotnetRef;
            if (!enabled) { return; }
            stopped = false;
            try { token = sessionStorage.getItem('ultima-live-token'); } catch (e) { token = null; }
            resume().then((ok) => { if (!ok && !stopped) { join(); } });
            window.addEventListener('pagehide', () => {
                if (!token) { return; }
                stopped = true;
                // keepalive: o navegador pode fechar antes da resposta.
                call('POST', '/v1/game/leave', {}, 3000, true).catch(() => { /* saída silenciosa */ });
            });
        },
        pose: pose,
        sendChat: sendChat,
        info: info,
        debug: info
    };
})();
