// The HTTP signaling service introduces room peers. WebRTC carries poses, chat and audio.
(function () {
    'use strict';

    const FALLBACK_API = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';
    const qs = new URLSearchParams(location.search);
    const API = (qs.get('api') || window.ULTIMA_API || FALLBACK_API).replace(/\/+$/, '');
    const room = ((qs.get('sala') || 'jardim').replace(/[^a-zA-Z0-9_-]/g, '').slice(0, 24)) || 'jardim';
    const enabled = qs.get('rede') !== '0' && !!window.RTCPeerConnection;
    // Identidade estável: sobrevive a F5. sessionStorage (por aba) tem prioridade; senão reaproveita
    // o ID salvo em localStorage, a menos que outra aba aberta o esteja usando (heartbeat).
    const myId = (function () {
        const fresh = () => Math.random().toString(16).slice(2, 10).padEnd(8, '0');
        const valid = (v) => typeof v === 'string' && /^[0-9a-f]{8}$/.test(v);
        let id = null;
        try {
            const tab = sessionStorage.getItem('ultima-id-aba');
            if (valid(tab)) { id = tab; }
            else {
                const saved = localStorage.getItem('ultima-id');
                let live = null;
                try { live = JSON.parse(localStorage.getItem('ultima-id-vivo') || 'null'); } catch (e) { live = null; }
                const busy = live && live.id === saved && Date.now() - live.ts < 6000;
                id = valid(saved) && !busy ? saved : fresh();
                if (!valid(saved)) { localStorage.setItem('ultima-id', id); }
            }
            sessionStorage.setItem('ultima-id-aba', id);
            const beat = () => { try { localStorage.setItem('ultima-id-vivo', JSON.stringify({ id: id, ts: Date.now() })); } catch (e) { /* sem storage */ } };
            beat();
            setInterval(beat, 2000);
        } catch (e) { id = id || fresh(); }
        return id;
    })();
    const ICE = { iceServers: [{ urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'] }] };
    const OFFER_RETRY_MS = 2500;
    const OFFER_RETRY_MAX = 24; // ~1 minuto tentando

    let dotnet = null, presence;
    let localStream = null;
    let stopped = false;
    const conns = new Map(); // pid -> conexão

    const remoteAudio = document.createElement('audio');
    remoteAudio.autoplay = true;

    function status(state) {
        if (dotnet) { dotnet.invokeMethodAsync('OnNetStatus', state, conns.size); }
    }

    // Nome: texto simples, sem controles, até 40 caracteres; vazio volta ao padrão.
    function cleanName(v) {
        return String(v == null ? '' : v).replace(/[\u0000-\u001f\u007f<>]/g, '').replace(/\s+/g, ' ').trim().slice(0, 40);
    }

    function playerName() {
        let saved = null;
        try { saved = localStorage.getItem('ultima-nome'); } catch (e) { /* sem storage */ }
        return cleanName(saved) || 'Jogador ' + myId.slice(0, 4);
    }

    function getName() {
        return playerName();
    }

    let nameTimer = 0;
    function setName(value) {
        const clean = cleanName(value);
        try {
            if (clean) { localStorage.setItem('ultima-nome', clean); } else { localStorage.removeItem('ultima-nome'); }
        } catch (e) { /* sem storage: vale só nesta sessão */ }
        // Espera o jogador parar de digitar antes de avisar peers e servidor.
        clearTimeout(nameTimer);
        nameTimer = setTimeout(() => {
            const hi = JSON.stringify({ t: 'hi', name: playerName() });
            for (const c of conns.values()) {
                if (c.channel && c.channel.readyState === 'open') {
                    try { c.channel.send(hi); } catch (e) { /* canal caindo */ }
                }
            }
            if (presence && !stopped) { join(); }
        }, 600);
        return clean;
    }

    async function api(path, body, timeoutMs) {
        const ctl = new AbortController();
        const timer = setTimeout(() => ctl.abort(), timeoutMs || 25000);
        try {
            const res = await fetch(API + path, {
                method: body === undefined ? 'GET' : 'POST',
                headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
                body: body === undefined ? undefined : JSON.stringify(body),
                cache: 'no-store',
                signal: ctl.signal
            });
            if (!res.ok) { throw new Error('http ' + res.status); }
            return await res.json();
        } finally {
            clearTimeout(timer);
        }
    }

    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    const sendSignal = (pid, kind, data) =>
        api('/v1/net/' + room + '/signal', { from: myId, to: pid, kind: kind, data: data }).catch(() => {});

    function toJs(desc) {
        if (!desc) { return null; }
        if (typeof desc.toJSON === 'function') { return desc.toJSON(); }
        return { type: desc.type, sdp: desc.sdp };
    }

    function sendPos(x, z, ry) {
        if (!presence || !conns.size) return;
        const packet = presence.pose(x, z, ry);
        for (const c of conns.values()) {
            if (c.channel && c.channel.readyState === 'open') {
                try { c.channel.send(packet); } catch (e) { /* canal caindo */ }
            }
        }
    }

    function sendChat(text) {
        const clean = String(text || '').slice(0, 300);
        if (!clean) { return; }
        for (const c of conns.values()) {
            if (c.channel && c.channel.readyState === 'open') {
                try { c.channel.send(JSON.stringify({ t: 'chat', text: clean })); } catch (e) { /* idem */ }
            }
        }
        if (window.game && game.live && game.live.sendChat) { game.live.sendChat(clean); }
    }

    function handleChannelMessage(pid, raw) {
        let msg;
        try { msg = JSON.parse(raw); } catch (e) { return; }
        const c = conns.get(pid);
        if (!c) { return; }
        if (msg.t === 'hi') {
            c.name = String(msg.name || 'Jogador').slice(0, 40);
            if (window.game) { game.renameRemote(pid, c.name); }
        } else if (msg.t === 'pos') {
            presence.update(pid, c.name, msg);
        } else if (msg.t === 'bye') {
            dropPeer(pid, false);
        } else if (msg.t === 'chat') {
            const text = String(msg.text || '').slice(0, 300);
            if (text && dotnet) { dotnet.invokeMethodAsync('OnChat', c.name || 'Jogador', text); }
        }
    }

    function stopOfferRetry(c) {
        if (c.offerTimer) { clearInterval(c.offerTimer); c.offerTimer = 0; }
    }

    // Reenvia a offer local até o canal abrir: sinalização HTTP pode perder mensagens
    // (por exemplo, quando o destino ainda não se registrou na sala).
    function startOfferRetry(pid, c) {
        stopOfferRetry(c);
        let tries = 0;
        c.offerTimer = setInterval(() => {
            tries += 1;
            const open = c.channel && c.channel.readyState === 'open';
            const answered = c.pc.remoteDescription && c.pc.remoteDescription.type === 'answer';
            if (open || answered || stopped || tries > OFFER_RETRY_MAX) { stopOfferRetry(c); return; }
            const desc = c.pc.localDescription;
            if (desc && desc.type === 'offer' && !c.settingRemoteAnswerPending) {
                sendSignal(pid, 'offer', toJs(desc));
            }
        }, OFFER_RETRY_MS);
    }

    function bindChannel(pid, channel) {
        const c = conns.get(pid);
        if (!c) { try { channel.close(); } catch (e) { /* já fechado */ } return; }
        c.channel = channel;
        channel.onopen = () => {
            stopOfferRetry(c);
            try { channel.send(JSON.stringify({ t: 'hi', name: playerName() })); } catch (e) { /* idem */ }
            status('Sala: ' + room);
        };
        channel.onmessage = (e) => handleChannelMessage(pid, e.data);
        channel.onclose = () => { if (conns.get(pid) === c) { dropPeer(pid, false); } };
    }

    function connect(pid) {
        if (conns.has(pid)) { return; }
        const c = {
            pc: new RTCPeerConnection(ICE),
            polite: myId < pid,
            makingOffer: false,
            ignoreOffer: false,
            settingRemoteAnswerPending: false,
            pendingIce: [],
            channel: null,
            name: null,
            offerTimer: 0,
            iceRestarts: 0
        };
        conns.set(pid, c);

        c.pc.onnegotiationneeded = async () => {
            try {
                c.makingOffer = true;
                await c.pc.setLocalDescription();
                sendSignal(pid, 'offer', toJs(c.pc.localDescription));
            } catch (e) { /* negociação falha não derruba o jogo */ }
            finally { c.makingOffer = false; }
        };
        c.pc.onicecandidate = (e) => { if (e.candidate) { sendSignal(pid, 'ice', e.candidate.toJSON()); } };
        c.pc.ontrack = (e) => {
            remoteAudio.srcObject = e.streams[0];
            remoteAudio.play().catch(() => { /* autoplay pode exigir gesto */ });
        };
        c.pc.ondatachannel = (e) => bindChannel(pid, e.channel);
        c.pc.onconnectionstatechange = () => {
            const s = c.pc.connectionState;
            if (s === 'failed') {
                if (c.iceRestarts < 2) {
                    c.iceRestarts += 1;
                    try { c.pc.restartIce(); } catch (err) { dropPeer(pid, false); }
                } else { dropPeer(pid, false); }
            } else if (s === 'closed') { dropPeer(pid, false); }
        };

        // Apenas o lado "impaciente" cria o canal e insiste com a offer;
        // o outro lado o recebe via ondatachannel e responde com answer.
        if (!c.polite) {
            bindChannel(pid, c.pc.createDataChannel('game'));
            startOfferRetry(pid, c);
        }
        if (localStream) {
            for (const track of localStream.getTracks()) { c.pc.addTrack(track, localStream); }
        }
        status('Sala: ' + room);
    }

    function flushIce(c) {
        const pending = c.pendingIce.splice(0);
        for (const candidate of pending) { c.pc.addIceCandidate(candidate).catch(() => { /* candidato velho */ }); }
    }

    async function handleSignal(m) {
        const pid = m && m.from;
        if (!pid || pid === myId) { return; }
        if (m.kind === 'bye') { dropPeer(pid, false); return; }
        if (!conns.has(pid)) { connect(pid); }
        const c = conns.get(pid);
        if (!c) { return; }
        try {
            if (m.kind === 'offer' && m.data) {
                const readyForOffer = !c.makingOffer &&
                    (c.pc.signalingState === 'stable' || c.settingRemoteAnswerPending);
                const offerCollision = m.data.type === 'offer' && !readyForOffer;
                c.ignoreOffer = !c.polite && offerCollision; // impaciente rejeita; polido aceita (rollback implícito)
                if (c.ignoreOffer) { return; }
                c.settingRemoteAnswerPending = m.data.type === 'answer';
                await c.pc.setRemoteDescription(m.data);
                c.settingRemoteAnswerPending = false;
                flushIce(c);
                if (m.data.type === 'offer') {
                    await c.pc.setLocalDescription();
                    sendSignal(pid, 'answer', toJs(c.pc.localDescription));
                }
            } else if (m.kind === 'answer' && m.data) {
                if (c.pc.signalingState === 'have-local-offer') {
                    c.settingRemoteAnswerPending = true;
                    await c.pc.setRemoteDescription(m.data);
                    c.settingRemoteAnswerPending = false;
                    flushIce(c);
                }
            } else if (m.kind === 'ice' && m.data) {
                if (c.pc.remoteDescription) {
                    await c.pc.addIceCandidate(m.data);
                } else {
                    c.pendingIce.push(m.data);
                }
            }
        } catch (e) { /* sinal ruim é descartado */ }
    }

    function dropPeer(pid, notify) {
        const c = conns.get(pid);
        if (!c) { return; }
        conns.delete(pid);
        stopOfferRetry(c);
        try { if (c.channel) { c.channel.close(); } } catch (e) { /* já fechado */ }
        try { c.pc.close(); } catch (e) { /* idem */ }
        if (notify) { sendSignal(pid, 'bye', null); }
        presence.remove(pid);
        status(conns.size ? 'Sala: ' + room : 'Sala: ' + room + ' (sozinho)');
    }

    function syncPeers(list) {
        const ids = Array.isArray(list) ? list.filter((x) => typeof x === 'string' && x !== myId) : [];
        for (const pid of ids) { if (!conns.has(pid)) { connect(pid); } }
        // Presença pode expirar (aba em segundo plano); só derruba quem não tem canal aberto.
        for (const pid of Array.from(conns.keys())) {
            const c = conns.get(pid);
            const open = c.channel && c.channel.readyState === 'open';
            if (!ids.includes(pid) && !open) { dropPeer(pid, false); }
        }
        status(conns.size ? 'Sala: ' + room : 'Sala: ' + room + ' (sozinho)');
    }

    async function join() {
        try {
            const res = await api('/v1/net/' + room + '/join', { peer: myId, name: playerName() }, 8000);
            syncPeers(res.peers);
            return true;
        } catch (e) {
            status('Offline (sinalização inacessível)');
            return false;
        }
    }

    async function pollLoop() {
        while (!stopped) {
            try {
                const res = await api('/v1/net/' + room + '/poll/' + myId + '?wait=15', undefined, 25000);
                if (res.expired) { await join(); continue; }
                syncPeers(res.peers);
                for (const m of res.messages || []) { await handleSignal(m); }
            } catch (e) {
                await sleep(4000);
                await join();
            }
        }
    }

    function attachStream(stream) {
        localStream = stream;
        for (const c of conns.values()) {
            for (const track of stream.getTracks()) {
                const sender = c.pc.getSenders().find((s) => s.track && s.track.kind === track.kind);
                if (sender) { sender.replaceTrack(track).catch(() => { /* substituição falhou */ }); }
                else { c.pc.addTrack(track, stream); }
            }
        }
    }

    function detachStream() {
        localStream = null;
        for (const c of conns.values()) {
            for (const sender of Array.from(c.pc.getSenders())) {
                if (sender.track && sender.track.kind === 'audio') {
                    try { c.pc.removeTrack(sender); } catch (e) { /* já removido */ }
                }
            }
        }
    }

    window.game.net = {
        async init(dotnetRef, environment) {
            dotnet = dotnetRef;
            if (!enabled) {
                status('P2P desligado (?rede=0)');
                return;
            }
            const { createRoomPresence } = await import('./room-presence.mjs?v=' + encodeURIComponent(game.buildStamp()));
            presence = createRoomPresence(environment, game);
            status('Conectando à sala ' + room + '…');
            join();
            pollLoop();
            setInterval(() => { if (!stopped) { join(); } }, 8000); // batimento/presença
            window.addEventListener('pagehide', () => {
                stopped = true;
                for (const { channel } of conns.values()) {
                    try { if (channel?.readyState === 'open') channel.send('{"t":"bye"}'); } catch { /* Channel already closed. */ }
                }
                api('/v1/net/' + room + '/leave', { peer: myId }, 3000).catch(() => { /* saída silenciosa */ });
            });
        },
        getName,
        setName,
        sendChat,
        sendPos,
        attachStream,
        detachStream,
        info() { return { id: myId, room: room, api: API, peers: Array.from(conns.keys()) }; },
        debug() {
            return {
                id: myId, room: room, api: API, stopped: stopped,
                peers: Array.from(conns.entries()).map(([pid, c]) => ({
                    pid: pid,
                    polite: c.polite,
                    channel: c.channel ? c.channel.readyState : null,
                    pc: c.pc.connectionState,
                    signaling: c.pc.signalingState,
                    recvAudio: c.pc.getReceivers().filter((r) => r.track && r.track.kind === 'audio')
                        .map((r) => r.track.readyState + (r.track.muted ? ':muted' : ':live')),
                    sendAudio: c.pc.getSenders().filter((s) => s.track && s.track.kind === 'audio').length,
                    name: c.name
                }))
            };
        }
    };
})();
