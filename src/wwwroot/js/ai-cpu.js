(function () {
    'use strict';
    // IA local da Guardiã — motor CPU (wllama/llama.cpp WASM, sem WebGPU).
    // Segundo motor do jogo: complementa o WebGPU (ai.js).
    // GitHub Pages não envia COOP/COEP => SharedArrayBuffer indisponível => sempre 1 thread
    // (decisão registrada em decisions/ia-cpu-wllama.md; coi-serviceworker ficou como pendência).
    const WLLAMA_ESM = 'https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/index.min.js';
    const WLLAMA_WASM = 'https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/wasm/wllama.wasm';
    const PREF = 'ultima-ia-cpu';
    const STALL_MS = 90000;     // sem progresso por 90 s => "parado em X%"
    const THINK_MS = 120000;    // geração sem fim => volta a "ready" e o servidor responde
    // Dois modelos, propostas diferentes (t48):
    // - qwen05b : melhor qualidade de conversa em português (Qwen2.5 0.5B Instruct Q4_K_M)
    // - gemma270: leveza/velocidade máxima em máquinas fracas (Gemma 3 270M Q4_K_M)
    const MODELS = {
        'gemma270': {
            url: 'https://huggingface.co/unsloth/gemma-3-270m-it-GGUF/resolve/main/gemma-3-270m-it-Q4_K_M.gguf',
            label: 'Gemma 3 270M', why: 'leveza e velocidade máxima em máquinas fracas', mb: 253115424 / 1048576
        },
        'qwen05b': {
            url: 'https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf',
            label: 'Qwen2.5 0.5B', why: 'melhor qualidade de conversa em português', mb: 491400032 / 1048576
        }
    };
    const PERSONA = 'Você é a Guardiã do Jardim do jogo Ultima: serena, acolhedora, sábia. Responda em português do Brasil, em primeira pessoa, com 2 a 4 frases curtas. Nunca saia do personagem.';

    let dotnet = null, inst = null, loading = null, source = '';
    let state = 'off', progress = 0, detail = '', modelKey = '', modelLabel = '', modelWhy = '';
    let loadedMB = 0, totalMB = 0, fromCache = false, speed = '';
    let lastReplyMs = null, tokensPerSec = null, lastReply = '';
    let stallTimer = null, thinkTimer = null, dlPrev = null;
    const history = [];
    const debug = { lastError: null, events: [] };

    function log(kind, data) {
        debug.events.push({ t: new Date().toISOString(), kind, data: String(data == null ? '' : data).slice(0, 200) });
        if (debug.events.length > 80) debug.events.shift();
    }
    function humanMB(mb) {
        return mb >= 1024 ? (mb / 1024).toFixed(2) + ' GB' : (mb >= 10 ? Math.round(mb) : mb.toFixed(1)) + ' MB';
    }
    function modelKeyDefault() {
        const q = new URLSearchParams(location.search).get('iacpu');
        if (q && MODELS[q]) return q;
        const saved = localStorage.getItem(PREF + '-modelo');
        return saved && MODELS[saved] ? saved : 'gemma270';
    }
    function emit(s, p, d) {
        state = s;
        if (typeof p === 'number' && isFinite(p)) progress = Math.max(0, Math.min(1, p));
        if (d !== undefined) detail = d;
        log('estado', state + ' ' + Math.round(progress * 100) + '% · ' + detail);
        if (dotnet) { try { dotnet.invokeMethodAsync('OnAiCpuState', state, progress, detail); } catch (e) { log('emit-falhou', e); } }
    }
    function beat(ms) {
        if (stallTimer) clearTimeout(stallTimer);
        stallTimer = setTimeout(function () {
            if (state === 'downloading' || state === 'loading') {
                log('travou', detail);
                emit('stalled', progress, 'Parado em ' + Math.round(progress * 100) + '% há um minuto e meio. Tente de novo ou use outro modo.');
            }
        }, ms || STALL_MS);
    }
    function stopWatchdog() { if (stallTimer) clearTimeout(stallTimer); stallTimer = null; }
    function onProgress(p) {
        const loaded = p && p.loaded, total = p && p.total;
        if (typeof loaded !== 'number' || !loaded) return;
        if (typeof total === 'number' && total > 0) {
            totalMB = total / 1048576;
            progress = Math.max(0, Math.min(1, loaded / total));
        }
        loadedMB = loaded / 1048576;
        fromCache = fromCache || (totalMB && loadedMB / totalMB > 0.9 && progressJumps());
        const now = performance.now();
        if (dlPrev && now > dlPrev.t && loadedMB > dlPrev.mb) {
            const v = (loadedMB - dlPrev.mb) / ((now - dlPrev.t) / 1000);
            if (v > 0) speed = v >= 1 ? v.toFixed(1) + ' MB/s' : Math.max(1, Math.round(v * 1024)) + ' KB/s';
        }
        dlPrev = { mb: loadedMB, t: now };
        beat();
        const extra = humanMB(loadedMB) + (totalMB ? ' de ' + humanMB(totalMB) : '') + ' · ' + (speed ? 'baixando · ' + speed : 'baixando');
        emit('downloading', progress, extra);
    }
    let jumps = 0, lastPct = -1;
    function progressJumps() {
        const pct = Math.round(progress * 100);
        if (lastPct >= 0 && pct - lastPct > 25) { jumps++; return jumps >= 2; }
        lastPct = pct; return false;
    }

    async function load() {
        if (loading) return loading;
        if (inst) return state === 'ready';
        loading = (async () => {
            try {
                const key = modelKeyDefault(), m = MODELS[key];
                modelKey = key; modelLabel = m.label; modelWhy = m.why;
                totalMB = m.mb; loadedMB = 0; fromCache = false; speed = ''; lastPct = -1; jumps = 0;
                emit('model', 0, 'Modelo escolhido: ' + m.label + ' — ' + m.why + ' (~' + humanMB(m.mb) + ' de download único, roda na CPU do seu aparelho).');
                emit('downloading', 0, 'Baixando ' + m.label + '…');
                beat();
                const mod = await import(WLLAMA_ESM);
                inst = new mod.Wllama({ 'default': WLLAMA_WASM });
                emit('downloading', progress, 'Baixando ' + m.label + '…');
                // n_gpu_layers: 0 => CPU pura; n_threads: 1 => single-thread (Pages não tem COOP/COEP)
                await inst.loadModelFromUrl(m.url, {
                    n_gpu_layers: 0, n_threads: 1, n_ctx: 4096, n_batch: 256,
                    progressCallback: onProgress, useCache: true
                });
                stopWatchdog();
                emit('loading', 1, 'Preparando ' + m.label + ' na memória…');
                localStorage.setItem(PREF, '1');
                emit('ready', 1, m.label + ' pronta na sua CPU (WASM, 1 thread)');
                log('modelo pronto', m.label);
                return true;
            } catch (e) {
                stopWatchdog();
                const tech = String(e && e.message || e).slice(0, 300);
                debug.lastError = { state: state, progress: progress, erro: tech, modelo: modelLabel };
                log('erro', tech);
                console.error('[ia-cpu] falha ao carregar modelo na CPU', e);
                try { if (inst) await inst.exit(); } catch { }
                inst = null;
                emit('error', progress, 'Não consegui carregar a IA na CPU' + (progress > 0 ? ' (parou em ' + Math.round(progress * 100) + '%)' : '') + '. A Guardiã pode responder pelo servidor — detalhe em game.aiCpu.debug().');
                return false;
            } finally { loading = null; }
        })();
        return loading;
    }
    async function retry() {
        log('retry', 'estado=' + state);
        if (loading) { beat(); emit('downloading', progress, 'Retomando… ' + detail); return loading; }
        return load();
    }
    function clean(text) {
        let t = String(text || '').replace(/<[^>]*>/g, '').replace(/\s+/g, ' ').trim();
        if (t.length > 300) { const cut = t.slice(0, 300); const end = Math.max(cut.lastIndexOf('. '), cut.lastIndexOf('! '), cut.lastIndexOf('? ')); t = end > 80 ? cut.slice(0, end + 1) : cut + '…'; }
        return t;
    }
    async function ask(text, player) {
        if (!inst) return null;
        const msg = String(text || '').slice(0, 300);
        const system = PERSONA + (player ? ' O jogador se chama ' + String(player).replace(/[<>]/g, '').slice(0, 40) + '.' : '') +
            ' Responda em português do Brasil, no máximo 4 frases curtas.';
        const messages = [{ role: 'system', content: system }].concat(history.slice(-6), [{ role: 'user', content: msg }]);
        emit('thinking', progress, 'Guardiã pensando na sua CPU…');
        if (thinkTimer) clearTimeout(thinkTimer);
        thinkTimer = setTimeout(function () {
            if (state === 'thinking') emit('ready', 1, 'A resposta na CPU demorou demais; o servidor responde por enquanto.');
        }, THINK_MS);
        try {
            const t0 = performance.now();
            const out = await inst.createChatCompletion({ messages: messages, max_tokens: 120, temperature: 0.7 });
            const reply = clean(out && out.choices && out.choices[0] && out.choices[0].message && out.choices[0].message.content);
            const ms = performance.now() - t0;
            lastReplyMs = Math.round(ms);
            const comp = out && out.usage && out.usage.completion_tokens;
            if (comp && comp > 2) tokensPerSec = Math.round((comp / (ms / 1000)) * 10) / 10;
            source = 'cpu';
            log('resposta', 'ms=' + lastReplyMs + ' tok/s=' + tokensPerSec + ' texto=' + (reply || '').slice(0, 80));
            if (!reply) return null;              // resposta vazia => servidor assume
            history.push({ role: 'user', content: msg }, { role: 'assistant', content: reply });
            while (history.length > 12) history.shift();
            lastReply = reply;
            return reply;
        } catch (e) {
            log('geração falhou', e && e.message || e);
            console.warn('[ia-cpu] falha na geração na CPU', e);
            return null;
        } finally {
            if (thinkTimer) clearTimeout(thinkTimer);
            if (inst && state === 'thinking') emit('ready', 1, modelLabel + ' pronta na sua CPU' + (tokensPerSec ? ' · ' + tokensPerSec + ' tok/s' : ''));
        }
    }
    async function garden(prompt, text, maxTokens = 1200, history = [], signal) {
        const model = inst;
        if (!model || state !== 'ready') return null;
        signal?.throwIfAborted();
        emit('thinking', progress, 'Guardiã pensando na sua CPU…');
        try {
            const messages = [{ role: 'system', content: prompt }];
            for (const turn of history) {
                messages.push({ role: 'user', content: turn.user }, { role: 'assistant', content: turn.assistant });
            }
            messages.push({ role: 'user', content: text });
            const output = await model.createChatCompletion({
                abortSignal: signal, messages, max_tokens: maxTokens, temperature: 0.2
            });
            signal?.throwIfAborted();
            return output?.choices?.[0]?.message?.content || null;
        } catch {
            return null;
        } finally {
            if (inst === model && state === 'thinking') emit('ready', 1, modelLabel + ' pronta na sua CPU');
        }
    }

    // Troca de modelo pelo painel: grava a escolha e descarrega o modelo anterior.
    async function setModel(key) {
        if (!MODELS[key] || loading) return false;
        localStorage.setItem(PREF + '-modelo', key);
        modelKey = key;
        if (inst) { try { await inst.exit(); } catch { } inst = null; }
        const m = MODELS[key];
        modelLabel = m.label; modelWhy = m.why;
        emit('model', 0, 'Modelo escolhido: ' + m.label + ' — ' + m.why + '.');
        return true;
    }
    async function init(dotnetRef) {
        dotnet = dotnetRef;
        modelKey = modelKeyDefault();
        const m = MODELS[modelKey];
        if (localStorage.getItem(PREF) === '1') { load(); }  // já usou antes: cache do navegador
        else emit('off', 0, 'Modelo sugerido: ' + m.label + ' (~' + humanMB(m.mb) + ' — ' + m.why + ').');
    }
    function disable() {
        stopWatchdog();
        localStorage.removeItem(PREF);
        try { if (inst) inst.exit(); } catch { }
        inst = null; history.length = 0; loadedMB = 0; speed = ''; tokensPerSec = null;
        emit('off', 0, 'IA na CPU desligada.');
    }

    window.game = window.game || {};
    window.game.aiCpu = {
        init: init, load: load, ask: ask, garden, retry: retry, disable: disable, setModel: setModel,
        debug: function () { return JSON.parse(JSON.stringify(debug)); },
        status: function () {
            return {
                engine: 'cpu-wllama', state: state, progress: progress, detail: detail,
                model: modelLabel, modelKey: modelKey, modelWhy: modelWhy,
                threads: 1, fromCache: fromCache, loadedMB: Math.round(loadedMB), totalMB: Math.round(totalMB),
                speed: speed, lastReplyMs: lastReplyMs, tokensPerSec: tokensPerSec, source: source,
                lastReply: lastReply
            };
        }
    };
})();
