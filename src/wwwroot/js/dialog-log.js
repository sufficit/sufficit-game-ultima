// Ultima — registro de conversas dos personagens (t50): captura cada turno de diálogo,
// guarda numa fila persistente (localStorage) e envia em lotes para a API de analytics.
//
//   • Captura: um único funil. O Blazor chama game.dialogLog.turn(...) quando a resposta
//     chega (roteiro do diálogo, chat da Guardiã com pergunta+resposta+origem, IA local);
//     internamente isso vira um CustomEvent 'ultima-dialog' na window — qualquer outro
//     produtor pode disparar o mesmo evento e cai na mesma fila.
//   • Fila: 'ultima-dialog-queue' (turnos pendentes, teto 500, gira) e
//     'ultima-dialog-history' (últimos ~50 turnos por NPC, só para depuração local).
//   • Envio: a cada 3 min (configurável) e no pagehide/visibilitychange(hidden) com
//     fetch keepalive; sendBeacon só como último recurso (sem confirmação — o servidor
//     deduplica por id). Remove da fila apenas os ids confirmados em { ok, ids }.
//   • Privacidade: mesmas regras do nome — sem < > nem controles, textos até ~500 chars;
//     nada de tokens de sessão, coordenadas ou dados de máquina. Só fala do jogo.
(function () {
    'use strict';

    const FALLBACK_API = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';
    const QKEY = 'ultima-dialog-queue';
    const HKEY = 'ultima-dialog-history';
    const MAX_TEXT = 500;

    const cfg = {
        intervalMs: 3 * 60 * 1000,   // cadência do envio periódico
        batchSize: 200,              // teto do servidor por lote
        maxQueue: 500,               // teto da fila pendente (gira: mais antigo sai)
        historyPerNpc: 50,
        enabled: true
    };

    let api = null;                  // resolvido no init (mesma fonte da rede)
    let endpoint = '/v1/analytics/dialogs';
    let timer = null;
    let sending = false;
    let retryAt = 0;                 // backoff: nada é enviado antes deste instante
    let failures = 0;
    let sentTotal = 0;
    let lastError = '';
    let lastSentAt = 0;
    let lastTurnKey = '';            // guarda barato contra dupla captura do mesmo turno

    // ---------- utilitários ----------

    // Mesma saneamento do nome no net.js: sem controles, sem < >, espaços colapsados.
    function clean(v, max) {
        return String(v == null ? '' : v)
            .replace(/[\u0000-\u001f\u007f<>]/g, '')
            .replace(/\s+/g, ' ')
            .trim()
            .slice(0, max);
    }

    // FNV-1a 32 bits: hash curto e determinístico do conteúdo do turno.
    function fnv(s) {
        let h = 2166136261;
        for (let i = 0; i < s.length; i++) {
            h ^= s.charCodeAt(i);
            h = Math.imul(h, 16777619);
        }
        return (h >>> 0).toString(16);
    }

    function buildStamp() {
        const meta = document.querySelector('meta[name="ultima-build"]');
        const v = meta ? meta.getAttribute('content') : '';
        return v && v.indexOf('__') !== 0 ? v.slice(0, 20) : 'dev';
    }

    function playerName() {
        try {
            const saved = localStorage.getItem('ultima-nome');
            if (saved) return clean(saved, 40);
        } catch (e) { /* sem storage */ }
        return 'Jogador';
    }

    // Origem da resposta → rótulo do modelo local (quando existir). Nada é consultado
    // além dos status já mantidos pelos módulos de IA.
    function modelOf(source) {
        try {
            if (source.indexOf('GPU do jogador') === 0 && window.game && game.ai && game.ai.status) {
                return clean(game.ai.status().model, 60);
            }
            if (source.indexOf('CPU do jogador') === 0 && window.game && game.aiCpu && game.aiCpu.status) {
                return clean(game.aiCpu.status().model, 60);
            }
        } catch (e) { /* status indisponível */ }
        return '';
    }

    function readJSON(key, dflt) {
        try {
            const v = JSON.parse(localStorage.getItem(key) || 'null');
            return v || dflt;
        } catch (e) { return dflt; }
    }

    function writeJSON(key, v) {
        try { localStorage.setItem(key, JSON.stringify(v)); } catch (e) { /* sem storage */ }
    }

    // ---------- fila ----------

    let queue = [];
    if (Array.isArray(readJSON(QKEY, null))) queue = readJSON(QKEY, []);

    function persist() {
        writeJSON(QKEY, queue);
        paint();
    }

    function history(npc, turn) {
        const h = readJSON(HKEY, {});
        const list = Array.isArray(h[npc]) ? h[npc] : [];
        list.push(turn);
        if (list.length > cfg.historyPerNpc) list.splice(0, list.length - cfg.historyPerNpc);
        h[npc] = list;
        const npcs = Object.keys(h);
        if (npcs.length > 24) delete h[npcs[0]]; // giro simples de NPCs antigos
        writeJSON(HKEY, h);
    }

    function enqueue(detail) {
        const npc = clean(detail.npc, 40);
        const player = clean(detail.player, MAX_TEXT);
        const reply = clean(detail.reply, MAX_TEXT);
        const source = clean(detail.source, 60);
        if (!npc || (!player && !reply)) return;
        const t = Date.now();
        const key = npc + '|' + player + '|' + reply;
        if (key === lastTurnKey && t - (queue.length ? queue[queue.length - 1].t : 0) < 2000) return;
        lastTurnKey = key;
        const turn = {
            id: 'd' + t.toString(36) + '-' + fnv(key),   // determinístico por turno
            t: t,
            npc: npc,
            player: player,
            reply: reply,
            source: source,
            model: modelOf(source),
            ms: Math.max(0, Math.min(600000, Math.round(Number(detail.ms) || 0)))
        };
        queue.push(turn);
        if (queue.length > cfg.maxQueue) queue.splice(0, queue.length - cfg.maxQueue);
        persist();
        history(npc, { t: t, player: player, reply: reply, source: source });
    }

    // Contador opcional no painel de debug que já existe (id renderizado só com HUD ligado).
    function paint() {
        const el = document.getElementById('ultima-dialog-queue-count');
        if (el) el.textContent = String(queue.length);
    }

    // ---------- envio ----------

    function backoff() {
        failures++;
        const ms = Math.min(30000 * Math.pow(2, failures - 1), 600000); // 30s → 10min
        retryAt = Date.now() + ms;
        return ms;
    }

    function removeConfirmed(ids) {
        const ack = new Set(ids);
        const kept = queue.filter(function (t) { return !ack.has(t.id); });
        const removed = queue.length - kept.length;
        queue = kept;
        persist();
        return removed;
    }

    async function sendBatch(batch, keepalive) {
        const body = JSON.stringify({ build: buildStamp(), player: playerName(), turns: batch });
        const res = await fetch(api + endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: body,
            keepalive: !!keepalive
        });
        if (!res.ok) throw new Error('http ' + res.status);
        const data = await res.json();
        if (data && data.ok === true && Array.isArray(data.ids)) return data.ids;
        throw new Error('bad_payload');
    }

    // Último recurso (navegador encerrando): sendBeacon só com Blob text/plain.
    // Sem resposta legível: a fila permanece e o servidor deduplica por id depois.
    function beaconBatch(batch) {
        try {
            const body = JSON.stringify({ build: buildStamp(), player: playerName(), turns: batch });
            return navigator.sendBeacon(api + endpoint, new Blob([body], { type: 'text/plain' }));
        } catch (e) { return false; }
    }

    async function flush(options) {
        const force = !!(options && options.force);
        if (!cfg.enabled || !api || sending || queue.length === 0) return false;
        if (!force && Date.now() < retryAt) return false;
        sending = true;
        const batch = queue.slice(0, cfg.batchSize);
        try {
            const ids = await sendBatch(batch, options && options.keepalive);
            const removed = removeConfirmed(ids);
            sentTotal += removed;
            lastSentAt = Date.now();
            failures = 0;
            retryAt = 0;
            lastError = '';
            return true;
        } catch (e) {
            lastError = String(e && e.message || e);
            if (force && beaconBatch(batch)) {
                // Enviado sem confirmação: mantém a fila; dedupe no servidor cobre.
                lastError = 'beacon_sem_confirmacao';
                return true;
            }
            backoff();
            return false;
        } finally {
            sending = false;
        }
    }

    // ---------- eventos ----------

    window.addEventListener('ultima-dialog', function (ev) {
        if (ev && ev.detail) enqueue(ev.detail);
    });

    function onLeaving() {
        if (!cfg.enabled || queue.length === 0) return;
        flush({ force: true, keepalive: true });
    }
    window.addEventListener('pagehide', onLeaving);
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'hidden') onLeaving();
    });

    // ---------- API pública ----------

    window.game = window.game || {};
    window.game.dialogLog = {
        // Chamado pelo Blazor no OnAfterRenderAsync; resolve a mesma API da rede.
        init: function () {
            const qs = new URLSearchParams(location.search);
            const overrides = window.ULTIMA_DIALOG_LOG || {};
            if (qs.get('dialogos') === '0') cfg.enabled = false;
            if (overrides.intervalMs) cfg.intervalMs = Math.max(15000, Number(overrides.intervalMs) || cfg.intervalMs);
            if (overrides.batchSize) cfg.batchSize = Math.min(200, Math.max(1, Number(overrides.batchSize) || cfg.batchSize));
            if (overrides.maxQueue) cfg.maxQueue = Math.min(2000, Math.max(10, Number(overrides.maxQueue) || cfg.maxQueue));
            if (overrides.enabled === false) cfg.enabled = false;
            if (overrides.endpoint) endpoint = String(overrides.endpoint);
            api = (overrides.api || qs.get('api') || window.ULTIMA_API ||
                (window.game && window.game.net && window.game.net.info ? window.game.net.info().api : '') ||
                FALLBACK_API).replace(/\/+$/, '');
            if (timer) clearInterval(timer);
            if (cfg.enabled) timer = setInterval(function () { flush(); }, cfg.intervalMs);
            paint();
        },
        // Funil único de captura: vira CustomEvent 'ultima-dialog'.
        turn: function (npc, player, reply, source, ms) {
            window.dispatchEvent(new CustomEvent('ultima-dialog', {
                detail: { npc: npc, player: player, reply: reply, source: source, ms: ms }
            }));
        },
        flush: flush,
        debug: function () {
            return {
                enabled: cfg.enabled, api: api + endpoint,
                queued: queue.length, sending: sending,
                intervalMs: cfg.intervalMs, retryInMs: Math.max(0, retryAt - Date.now()),
                sentTotal: sentTotal, lastSentAt: lastSentAt, lastError: lastError
            };
        }
    };
})();
