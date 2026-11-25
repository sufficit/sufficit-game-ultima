// IA local da Guardiã: modelo de linguagem rodando na GPU do jogador (WebGPU + WebLLM).
// Nada sai do navegador: o modelo é baixado uma vez (cache do navegador) e executado localmente.
// A CPU ou a IA configurada no servidor podem atender quando a GPU estiver indisponível.
// Todos os estados são reportados ao painel via OnAiState e ficam visíveis em window.game.ai.status().
// Estados: checking, unsupported, off, model, downloading, compiling, stalled, ready, thinking, error.
(function () {
    'use strict';
    const WEBLLM = 'https://esm.run/@mlc-ai/web-llm@0.2.89';
    const API = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';
    const PREF = 'ultima-ia-local';
    const STALL_MS = 60000;      // sem progresso por 60 s => "parado em X%"
    const THINK_MS = 65000;      // geração local sem fim => volta a "ready" e o servidor responde
    const MODELS = {
        '1.5b': { f16: 'Qwen2.5-1.5B-Instruct-q4f16_1-MLC', f32: 'Qwen2.5-1.5B-Instruct-q4f32_1-MLC', label: 'Qwen2.5 1.5B', mb: 1000 },
        '0.5b': { f16: 'Qwen2.5-0.5B-Instruct-q4f16_1-MLC', f32: 'Qwen2.5-0.5B-Instruct-q4f32_1-MLC', label: 'Qwen2.5 0.5B', mb: 400 },
        'smol': { f16: 'SmolLM2-135M-Instruct-q0f16-MLC', f32: 'SmolLM2-135M-Instruct-q0f32-MLC', label: 'SmolLM2 135M', mb: 270 }
    };
    const FALLBACK_PERSONA = 'Você é a Guardiã do Jardim, uma figura serena e acolhedora de um jogo 3D chamado Ultima. Fale sempre em português do Brasil, em primeira pessoa, com no máximo 2 frases curtas. Nunca saia do personagem.';

    let dotnet = null, engine = null, loading = null;
    let state = 'off', progress = 0, detail = '', modelLabel = '', modelWhy = '', f16 = false;
    let adapterOk = null, adapterInfo = '';
    let persona = null, facts = [];
    let fromCache = false, loadedMB = 0, totalMB = 0, speed = '', lastReplyMs = null, source = '';
    const history = [];
    const debug = { lastError: null, lastProgressText: '', events: [] };
    let stallTimer = null, thinkTimer = null, dlPrev = null;

    function log(kind, data) {
        debug.events.push({ t: new Date().toISOString(), kind, data: String(data == null ? '' : data).slice(0, 200) });
        if (debug.events.length > 80) debug.events.shift();
    }
    function humanMB(mb) {
        return mb >= 1024 ? (mb / 1024).toFixed(2) + ' GB' : (mb >= 10 ? Math.round(mb) : mb.toFixed(1)) + ' MB';
    }
    function sizeKey() {
        const q = new URLSearchParams(location.search).get('ia');
        if (q && MODELS[q]) return q;
        const saved = localStorage.getItem(PREF + '-modelo');
        return saved && MODELS[saved] ? saved : '1.5b';
    }
    function emit(s, p, d) {
        state = s;
        if (typeof p === 'number' && isFinite(p)) progress = Math.max(0, Math.min(1, p));
        if (d !== undefined) detail = d;
        log('estado', state + ' ' + Math.round(progress * 100) + '% · ' + detail);
        if (dotnet) { try { dotnet.invokeMethodAsync('OnAiState', state, progress, detail); } catch (e) { log('emit-falhou', e); } }
    }

    // --- Verificação de WebGPU, com motivo compreensível quando falta ---
    async function probe() {
        if (adapterOk !== null) return adapterOk;
        emit('checking', 0, 'Verificando suporte a WebGPU no seu navegador…');
        try {
            if (!navigator.gpu) {
                adapterOk = false; adapterInfo = 'navigator.gpu ausente';
                emit('unsupported', 0, 'Este navegador não tem WebGPU (use Chrome/Edge 113+). Você pode usar outro provedor de IA.');
                return false;
            }
            const a = await navigator.gpu.requestAdapter();
            if (!a) {
                adapterOk = false; adapterInfo = 'requestAdapter devolveu nulo (nenhuma placa aceitou)';
                emit('unsupported', 0, 'O navegador tem WebGPU, mas nenhuma placa de vídeo aceitou o trabalho. Você pode usar outro provedor de IA.');
                return false;
            }
            adapterOk = true;
            f16 = !!a.features.has('shader-f16');
            let maxMB = 0;
            try { if (a.limits && a.limits.maxBufferSize) maxMB = Math.round(a.limits.maxBufferSize / 1048576); } catch { }
            adapterInfo = 'shader-f16=' + f16 + (maxMB ? ' · buffer máx ' + maxMB + ' MB' : '');
            log('adaptador', adapterInfo);
        } catch (e) {
            adapterOk = false; adapterInfo = 'exceção ao consultar a placa: ' + (e && e.message || e);
            emit('unsupported', 0, 'Não consegui consultar sua placa de vídeo. Você pode usar outro provedor de IA.');
            return false;
        }
        return true;
    }

    // --- Relógio de progresso: download parado 60 s ou compilação parada 180 s => "parado em X%" ---
    function beat(ms) {
        if (stallTimer) clearTimeout(stallTimer);
        stallTimer = setTimeout(function () {
            if (state === 'downloading' || state === 'compiling') {
                log('travou', detail);
                emit('stalled', progress, 'Parado em ' + Math.round(progress * 100) + '% há um minuto. Tente de novo ou use o servidor.');
            }
        }, ms || STALL_MS);
    }
    function stopWatchdog() { if (stallTimer) clearTimeout(stallTimer); stallTimer = null; }

    // --- Progresso do WebLLM: fase, %, MB baixados, velocidade e cache ---
    function onProgress(r) {
        const p = Math.max(0, Math.min(1, r.progress || 0));
        const text = String(r.text || '');
        debug.lastProgressText = text;
        const compiling = /shader|compil/i.test(text);
        fromCache = fromCache || (/cache/i.test(text) && !/fetch/i.test(text));
        const m = text.match(/([\d.]+)\s*(KB|MB|GB)\s*fetched/i);
        if (m) {
            const unit = m[2].toUpperCase();
            loadedMB = parseFloat(m[1]) * (unit === 'GB' ? 1024 : unit === 'KB' ? 1 / 1024 : 1);
            const now = performance.now();
            if (dlPrev && now > dlPrev.t && loadedMB > dlPrev.mb) {
                const v = (loadedMB - dlPrev.mb) / ((now - dlPrev.t) / 1000);
                if (v > 0) speed = v >= 1 ? v.toFixed(1) + ' MB/s' : Math.max(1, Math.round(v * 1024)) + ' KB/s';
            }
            dlPrev = { mb: loadedMB, t: now };
        }
        beat(compiling ? 180000 : STALL_MS);
        if (compiling) { emit('compiling', p, 'Compilando o modelo na sua GPU…'); return; }
        const extra = loadedMB
            ? humanMB(loadedMB) + (totalMB ? ' de ~' + humanMB(totalMB) : '') + ' · ' + (fromCache ? 'cache do navegador' : (speed ? 'baixando · ' + speed : 'baixando'))
            : text.replace(/\s+/g, ' ').slice(0, 90);
        emit('downloading', p, extra);
    }

    async function loadPersona() {
        if (persona) return;
        try {
            const r = await fetch(API + '/v1/npcs/guardia', { cache: 'no-store' });
            const j = await r.json();
            if (typeof j.persona === 'string' && j.persona.length < 4000) persona = j.persona;
            if (Array.isArray(j.facts)) facts = j.facts.filter(function (f) { return typeof f === 'string' && f.length < 500; }).slice(0, 20);
        } catch { /* sem API: persona embutida */ }
        if (!persona) persona = FALLBACK_PERSONA;
    }
    function clean(text) {
        let t = String(text || '').replace(/<[^>]*>/g, '').replace(/\s+/g, ' ').trim();
        if (t.length > 300) { const cut = t.slice(0, 300); const end = Math.max(cut.lastIndexOf('. '), cut.lastIndexOf('! '), cut.lastIndexOf('? ')); t = end > 80 ? cut.slice(0, end + 1) : cut + '…'; }
        return t;
    }

    async function load() {
        if (engine) { emit('ready', 1, modelLabel + ' pronta na sua GPU'); return true; }
        if (loading) return loading;
        loading = (async () => {
            try {
                if (!(await probe())) return false;
                const key = sizeKey(), m = MODELS[key];
                const id = f16 ? m.f16 : m.f32;
                modelLabel = m.label;
                totalMB = m.mb;
                modelWhy = m.label + ' ' + (f16 ? 'q4f16 — sua GPU acelera meia precisão' : 'q4f32 — modo compatível com sua GPU');
                emit('model', 0, 'Modelo escolhido: ' + modelWhy + ' (~' + humanMB(m.mb) + ' de download único).');
                await loadPersona();
                emit('downloading', 0, 'Baixando ' + m.label + '…');
                beat();
                const webllm = await import(WEBLLM);
                engine = await webllm.CreateMLCEngine(id, { initProgressCallback: onProgress });
                stopWatchdog();
                localStorage.setItem(PREF, '1');
                emit('ready', 1, modelLabel + ' pronta na sua GPU (' + (f16 ? 'q4f16' : 'q4f32') + ')');
                return true;
            } catch (e) {
                stopWatchdog();
                const tech = String(e && e.message || e).slice(0, 300);
                debug.lastError = { state: state, progress: progress, erro: tech, adaptador: adapterInfo, progressoTexto: debug.lastProgressText };
                log('erro', tech);
                console.error('[ia] falha ao carregar modelo local', e);
                engine = null;
                emit('error', progress, 'Não consegui carregar a IA local' + (progress > 0 ? ' (parou em ' + Math.round(progress * 100) + '%)' : '') + '. A Guardiã pode responder pelo servidor — detalhe técnico em game.ai.debug().');
                return false;
            } finally { loading = null; }
        })();
        return loading;
    }

    // "Tentar de novo": se a carga anterior ainda corre, só retoma o relógio; se morreu, recomeça.
    async function retry() {
        log('retry', 'estado=' + state);
        if (engine) { emit('ready', 1, modelLabel + ' pronta na sua GPU'); return true; }
        if (loading) { beat(); emit('downloading', progress, 'Retomando… ' + detail); return loading; }
        return load();
    }

    async function ask(text, player) {
        if (!engine) return null;
        const msg = String(text || '').slice(0, 300);
        const system = persona + '\nFatos do mundo:\n- ' + facts.join('\n- ') +
            (player ? '\nO jogador se chama ' + String(player).replace(/[<>]/g, '').slice(0, 40) + '.' : '') +
            '\nResponda sempre em português do Brasil, no máximo 2 frases curtas.';
        const messages = [{ role: 'system', content: system }].concat(history.slice(-8), [{ role: 'user', content: msg }]);
        emit('thinking', progress, 'Guardiã pensando na sua GPU…');
        if (thinkTimer) clearTimeout(thinkTimer);
        thinkTimer = setTimeout(function () {
            if (state === 'thinking') emit('ready', 1, 'A resposta local demorou demais; o servidor responde por enquanto.');
        }, THINK_MS);
        try {
            const t0 = performance.now();
            const out = await engine.chat.completions.create({ messages: messages, max_tokens: 120, temperature: 0.7 });
            const reply = clean(out.choices && out.choices[0] && out.choices[0].message && out.choices[0].message.content);
            lastReplyMs = Math.round(performance.now() - t0);
            source = 'gpu';
            console.info('[ia] resposta local em', lastReplyMs, 'ms');
            if (!reply) return null;
            history.push({ role: 'user', content: msg }, { role: 'assistant', content: reply });
            while (history.length > 16) history.shift();
            return reply;
        } catch (e) {
            log('geração falhou', e && e.message || e);
            console.warn('[ia] falha na geração local', e);
            return null;
        } finally {
            if (thinkTimer) clearTimeout(thinkTimer);
            if (engine && state === 'thinking') emit('ready', 1, modelLabel + ' pronta na sua GPU');
        }
    }

    async function garden(prompt, text, maxTokens = 1200, history = [], signal) {
        const model = engine;
        if (!model || state !== 'ready') return null;
        signal?.throwIfAborted();
        emit('thinking', progress, 'Guardiã pensando na sua GPU…');
        const interrupt = () => { void model.interruptGenerate().catch(() => {}); };
        signal?.addEventListener('abort', interrupt, { once: true });
        try {
            const messages = [{ role: 'system', content: prompt }];
            for (const turn of history) {
                messages.push({ role: 'user', content: turn.user }, { role: 'assistant', content: turn.assistant });
            }
            messages.push({ role: 'user', content: text });
            const output = await model.chat.completions.create({
                messages, max_tokens: maxTokens, temperature: 0.2
            });
            signal?.throwIfAborted();
            return output?.choices?.[0]?.message?.content || null;
        } catch {
            return null;
        } finally {
            signal?.removeEventListener('abort', interrupt);
            if (engine === model && state === 'thinking') emit('ready', 1, modelLabel + ' pronta na sua GPU');
        }
    }

    async function init(dotnetRef) {
        dotnet = dotnetRef;
        const ok = await probe(); // probe já emite 'unsupported' com o motivo quando falha
        if (!ok) return;
        if (localStorage.getItem(PREF) === '1') { load(); } // já ativou antes: modelo vem do cache
        else {
            const m = MODELS[sizeKey()];
            emit('off', 0, 'Modelo sugerido: ' + m.label + ' (~' + humanMB(m.mb) + ', baixado uma única vez).');
        }
    }
    function disable() {
        stopWatchdog();
        localStorage.removeItem(PREF);
        if (engine) { try { engine.unload(); } catch { } }
        engine = null; history.length = 0; fromCache = false; loadedMB = 0; speed = '';
        emit('off', 0, 'IA na GPU desligada.');
    }

    window.game = window.game || {};
    window.game.ai = {
        init: init, load: load, ask: ask, garden, retry: retry, disable: disable,
        debug: function () { return JSON.parse(JSON.stringify(debug)); },
        status: function () {
            return {
                state: state, progress: progress, detail: detail,
                model: modelLabel, modelWhy: modelWhy, f16: f16,
                webgpu: adapterOk, gpu: adapterInfo,
                fromCache: fromCache, loadedMB: Math.round(loadedMB), totalMB: totalMB,
                speed: speed, lastReplyMs: lastReplyMs, source: source
            };
        }
    };
})();
