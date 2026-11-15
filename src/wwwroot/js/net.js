// Ultima — multiplayer P2P: WebRTC DataChannel (posições + chat) e faixa de áudio (voz),
// com sinalização HTTP opcional. A API só apresenta os navegadores; o tráfego é direto.
(function () {
    'use strict';

    const FALLBACK_API = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';
    const qs = new URLSearchParams(location.search);
    const API = (qs.get('api') || window.ULTIMA_API || FALLBACK_API).replace(/\/+$/, '');
    const room = ((qs.get('sala') || 'jardim').replace(/[^a-zA-Z0-9_-]/g, '').slice(0, 24)) || 'jardim';
    const enabled = qs.get('rede') !== '0' && !!window.RTCPeerConnection;
    const myId = Math.random().toString(16).slice(2, 10);
    const ICE = { iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] };

    let dotnet = null;
    let localStream = null;
    let stopped = false;
    const conns = new Map(); // pid -> conexão

    const remoteAudio = document.createElement('audio');
    remoteAudio.autoplay = true;

    function status(state) {
        if (dotnet) { dotnet.invokeMethodAsync('OnNetStatus', state, conns.size); }
    }

    function playerName() {
        let saved = null;
        try { saved = localStorage.getItem('ultima-nome'); } catch (e) { /* sem storage */ }
        return (saved || 'Jogador ' + myId.slice(0, 4)).slice(0, 40);
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
        for (const c of conns.values()) {
            if (c.channel && c.channel.readyState === 'open') {
                try { c.channel.send(JSON.stringify({ t: 'pos', x: x, z: z, ry: ry })); } catch (e) { /* canal caindo */ }
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
    }

    function handleChannelMessage(pid, raw) {
        let msg;
        try { msg = JSON.parse(raw); } catch (e) { return; }
        const c = conns.get(pid);
        if (!c) { return; }
        if (msg.t === 'hi') {
            c.name = String(msg.name || 'Jogador').slice(0, 40);
            if (window.game) { game.addRemote(pid, c.name); }
        } else if (msg.t === 'pos') {
            if (window.game) { game.updateRemote(pid, Number(msg.x) || 0, Number(msg.z) || 0, Number(msg.ry) || 0); }
        } else if (msg.t === 'chat') {
            const text = String(msg.text || '').slice(0, 300);
            if (text && dotnet) { dotnet.invokeMethodAsync('OnChat', c.name || 'Jogador', text); }
        }
    }

    function bindChannel(pid, channel) {
        const c = conns.get(pid);
        if (!c) { try { channel.close(); } catch (e) { /* já fechado */ } return; }
        c.channel = channel;
        channel.onopen = () => {
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
            name: null
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
            if (c.pc.connectionState === 'failed' || c.pc.connectionState === 'closed') { dropPeer(pid, false); }
        };

        // Apenas o lado "impaciente" cria o canal; o outro o recebe via ondatachannel.
        if (!c.polite) { bindChannel(pid, c.pc.createDataChannel('game')); }
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
        try { if (c.channel) { c.channel.close(); } } catch (e) { /* já fechado */ }
        try { c.pc.close(); } catch (e) { /* idem */ }
        if (notify) { sendSignal(pid, 'bye', null); }
        if (window.game) { game.removeRemote(pid); }
        status(conns.size ? 'Sala: ' + room : 'Sala: ' + room + ' (sozinho)');
    }

    function syncPeers(list) {
        const ids = Array.isArray(list) ? list.filter((x) => typeof x === 'string' && x !== myId) : [];
        for (const pid of ids) { if (!conns.has(pid)) { connect(pid); } }
        for (const pid of Array.from(conns.keys())) { if (!ids.includes(pid)) { dropPeer(pid, false); } }
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
        init(dotnetRef) {
            dotnet = dotnetRef;
            if (!enabled) {
                status('P2P desligado (?rede=0)');
                return;
            }
            status('Conectando à sala ' + room + '…');
            join();
            pollLoop();
            setInterval(() => { if (!stopped) { join(); } }, 8000); // batimento/presença
            window.addEventListener('pagehide', () => {
                stopped = true;
                api('/v1/net/' + room + '/leave', { peer: myId }, 3000).catch(() => { /* saída silenciosa */ });
            });
        },
        sendChat,
        sendPos,
        attachStream,
        detachStream,
        info() { return { id: myId, room: room, api: API, peers: Array.from(conns.keys()) }; }
    };
})();
