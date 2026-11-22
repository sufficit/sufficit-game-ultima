// Ultima — shell do motor: decide onde o render roda e faz as pontes.
//
//   • Modo worker (padrão quando o navegador suporta): o motor inteiro (game-core.js)
//     roda num Web Worker com o canvas transferido (OffscreenCanvas). A main thread
//     fica só com UI/input/rede — teclado, gamepad, analógicos virtuais e câmera são
//     capturados aqui e postados; eventos do motor chegam por mensagem e viram
//     invocações no .NET (JSInterop); diagnósticos (status/report) usam request id.
//   • Fallback (?render=main, sem OffscreenCanvas/Worker, ou worker falhando no boot):
//     o Babylon é injetado com SRI e o game-core roda aqui mesmo, como sempre rodou.
//
// O contrato window.game.* é o mesmo nos dois modos; game.init(canvasId, dotnetRef)
// permanece a porta de entrada chamada pelo Blazor.
(function () {
    'use strict';

    const BABYLON_CDN = 'https://cdn.jsdelivr.net/npm/babylonjs@9.36.0/babylon.js';
    const BABYLON_SRI = 'sha384-QWFG07sio34fL3sNY9wd1SSoDvD7YVMX3067rJICw3vO2LQQY5m53yB70YlKXnRX';
    const FORCE = new URLSearchParams(location.search).get('render');   // 'worker' | 'main' | null

    let dotnet = null;
    let dotnetModels = null;    // ponte de variação paramétrica (ModelsInterop.VaryModel)
    let core = null;            // api do game-core no modo main
    let worker = null;
    let mode = 'main';
    const calls = new Map();    // request id -> resolve (diagnósticos no modo worker)
    let callSeq = 0;

    const SCENERY_PREF_KEY = 'ultima.cenario';
    function sceneryPref() {
        try { return localStorage.getItem(SCENERY_PREF_KEY) === 'lowpoly' ? 'lowpoly' : 'procedural'; }
        catch (e) { return 'procedural'; }
    }

    function buildStamp() {
        const meta = document.querySelector('meta[name="ultima-build"]');
        return (meta && meta.content) || 'dev';
    }

    // Variação paramétrica: o core pede, o shell chama o C# e devolve o descritor JSON.
    function varyModelAsync(id, seed, detail) {
        if (!dotnetModels) { return Promise.reject(new Error('ponte de modelos indisponível')); }
        return dotnetModels.invokeMethodAsync('VaryModel', id, seed | 0, Number(detail) || 1);
    }

    // Eventos do motor → .NET (o mesmo caminho do invokeMethodAsync antigo).
    function postEventToDotnet(name, args) {
        if (!dotnet) { return; }
        dotnet.invokeMethodAsync.apply(dotnet, [name].concat(args || [])).catch(function () { /* UI ainda subindo */ });
    }

    // Pose do jogador → rede P2P e presença (módulos da main thread).
    function sendPose(x, z, ry) {
        const g = window.game;
        if (g && g.net && g.net.sendPos) { g.net.sendPos(x, z, ry); }
        if (g && g.live && g.live.pose) { g.live.pose(x, z, ry); }
    }

    function onInputMode(kind) {
        if (document.body) { document.body.dataset.input = kind; }
    }

    // ---------- Modo main: Babylon injetado com SRI, core direto ----------
    function loadBabylon() {
        if (window.BABYLON) { return Promise.resolve(); }
        return new Promise(function (resolve, reject) {
            const s = document.createElement('script');
            s.src = BABYLON_CDN;
            s.integrity = BABYLON_SRI;
            s.crossOrigin = 'anonymous';
            s.onload = resolve;
            s.onerror = function () { reject(new Error('Babylon.js não carregou do CDN')); };
            document.head.appendChild(s);
        });
    }

    function startMain(canvas) {
        return loadBabylon().then(function () {
            return new Promise(function (resolve, reject) {
                mode = 'main';
                core = window.UltimaCore.create({
                    canvas: canvas,
                    mode: 'main',
                    build: buildStamp(),
                    pageBase: location.href,
                    scenery: sceneryPref(),
                    global: window,
                    dom: true,
                    postEvent: postEventToDotnet,
                    sendPose: sendPose,
                    onInputMode: onInputMode,
                    varyModel: varyModelAsync,
                    onCoreReady: function () { resolve(true); },
                    onCoreError: reject
                });
            });
        });
    }

    // ---------- Modo worker: canvas transferido + ponte de mensagens ----------
    function startWorker(canvas) {
        const off = canvas.transferControlToOffscreen();
        worker = new Worker('js/game-worker.js');
        return new Promise(function (resolve, reject) {
            const fatal = setTimeout(function () { reject(new Error('worker não deu sinal de vida')); }, 20000);
            worker.onmessage = function (e) { handleWorkerMessage(e.data, resolve, reject, fatal); };
            worker.onerror = function (e) {
                clearTimeout(fatal);
                reject(new Error('worker: ' + (e && e.message || 'erro')));
            };
            worker.postMessage(
                { type: 'init', canvas: off, width: canvas.width || canvas.clientWidth || 1280, height: canvas.height || canvas.clientHeight || 720, build: buildStamp(), pageBase: location.href, scenery: sceneryPref() },
                [off]);
        });
    }

    function handleWorkerMessage(msg, resolve, reject, fatal) {
        if (!msg || !msg.type) { return; }
        if (msg.type === 'ready') {
            clearTimeout(fatal);
            mode = 'worker';
            resolve(true);
        } else if (msg.type === 'fatal') {
            clearTimeout(fatal);
            reject(new Error(msg.message || 'boot falhou no worker'));
        } else if (msg.type === 'event') {
            // OnEngineReady ganha o modo no HUD; o resto segue igual.
            if (msg.name === 'OnEngineReady') {
                postEventToDotnet('OnEngineReady', [String(msg.args[0]) + ' · render em worker']);
            } else {
                postEventToDotnet(msg.name, msg.args);
            }
        } else if (msg.type === 'pose') {
            sendPose(msg.x, msg.z, msg.ry);
        } else if (msg.type === 'inputMode') {
            onInputMode(msg.kind);
        } else if (msg.type === 'result') {
            const r = calls.get(msg.id);
            if (r) { calls.delete(msg.id); r(msg.value); }
        } else if (msg.type === 'varyModel') {
            // Core pediu variação paramétrica: chama o C# e devolve o descritor JSON ao worker.
            varyModelAsync(msg.modelId, msg.seed, msg.detail).then(function (json) {
                worker.postMessage({ type: 'varyResult', reqId: msg.reqId, json: json || null });
            }, function (err) {
                worker.postMessage({ type: 'varyResult', reqId: msg.reqId, json: null, error: String(err && err.message || err) });
            });
        }
    }

    // Comando fire-and-forget: no worker vira mensagem; no main chama o core direto.
    function post(method, args) {
        args = args || [];
        if (worker) { worker.postMessage({ type: 'call', method: method, args: args }); return; }
        if (core) {
            const parts = method.split('.');
            let obj = core;
            for (const part of parts.slice(0, -1)) { obj = obj[part]; }
            obj[parts[parts.length - 1]].apply(obj, args);
        }
    }

    // Diagnóstico com resposta (status/report/get) — Promise no modo worker,
    // valor direto no modo main (scripts de validação esperam qualquer um).
    function query(method, args) {
        if (mode === 'main' && core) {
            const parts = method.split('.');
            let obj = core;
            for (const part of parts.slice(0, -1)) { obj = obj[part]; }
            return Promise.resolve(obj[parts[parts.length - 1]].apply(obj, args || []));
        }
        if (!worker) { return Promise.resolve(null); }
        return new Promise(function (resolve) {
            const id = ++callSeq;
            calls.set(id, resolve);
            worker.postMessage({ type: 'call', method: method, args: args || [], id: id });
        });
    }

    // ---------- Input capturado aqui e entregue ao core (qualquer modo) ----------
    const gameCodes = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ShiftLeft', 'ShiftRight'];
    const editable = (el) => !!(el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName)));

    function deliver(method, args) {
        if (mode === 'main' && core) { core.input[method].apply(null, args); }
        else if (worker) { worker.postMessage({ type: 'input', method: method, args: args }); }
    }

    function setupInput(canvas) {
        window.addEventListener('keydown', function (e) {
            deliver('inputMode', ['keyboard']);
            if (editable(e.target)) {
                if (e.code === 'Escape') { e.preventDefault(); postEventToDotnet('OnClose', []); }
                return;
            }
            if (gameCodes.includes(e.code)) { deliver('key', [e.code, true]); e.preventDefault(); }
            else if (e.code === 'KeyE') { e.preventDefault(); deliver('action', []); }
            else if (e.code === 'Escape') { e.preventDefault(); postEventToDotnet('OnClose', []); }
        });
        window.addEventListener('keyup', function (e) {
            if (gameCodes.includes(e.code)) { deliver('key', [e.code, false]); e.preventDefault(); }
        });
        window.addEventListener('blur', function () { deliver('clear', []); });

        // Gamepad (não existe em worker): o poll roda aqui, os eixos vão pela ponte.
        let actionWasDown = false, cancelWasDown = false;
        const DEADZONE = 0.18;
        function radial(x, y) {
            const m = Math.hypot(x, y);
            if (m < DEADZONE) { return { x: 0, y: 0 }; }
            const scaled = Math.min(1, (m - DEADZONE) / (1 - DEADZONE));
            return { x: (x / m) * scaled, y: (y / m) * scaled };
        }
        (function pollPad() {
            window.requestAnimationFrame(pollPad);
            let pad = null;
            try {
                const pads = navigator.getGamepads ? navigator.getGamepads() : [];
                pad = Array.from(pads || []).find(function (p) { return p && p.connected; });
            } catch (e) { /* Gamepad API indisponível */ }
            if (!pad) { return; }
            const l = radial(pad.axes[0] || 0, pad.axes[1] || 0);
            const r = radial(pad.axes[2] || 0, pad.axes[3] || 0);
            let x = l.x, y = -l.y;
            const down = function (i) { return !!(pad.buttons[i] && pad.buttons[i].pressed); };
            if (down(14)) { x -= 1; } if (down(15)) { x += 1; }
            if (down(12)) { y += 1; } if (down(13)) { y -= 1; }
            if (x || y || r.x || r.y) { deliver('inputMode', ['gamepad']); }
            deliver('pad', [x, y, r.x, r.y]);
            const action = down(0), cancel = down(1) || down(9);
            if (action && !actionWasDown) { deliver('action', []); }
            if (cancel && !cancelWasDown) { postEventToDotnet('OnClose', []); }
            actionWasDown = action; cancelWasDown = cancel;
        })();

        // Câmera no modo worker: arrasto e roda no canvas viram deltas (a attachControl
        // do Babylon pertence ao core na main). No modo main o core cuida disso sozinho.
        let dragging = false, lastX = 0, lastY = 0;
        canvas.addEventListener('pointerdown', function (e) { dragging = true; lastX = e.clientX; lastY = e.clientY; });
        window.addEventListener('pointerup', function () { dragging = false; });
        window.addEventListener('pointermove', function (e) {
            if (!dragging) { return; }
            deliver('camera', [e.clientX - lastX, e.clientY - lastY, 0]);
            lastX = e.clientX; lastY = e.clientY;
        });
        canvas.addEventListener('wheel', function (e) {
            e.preventDefault();
            deliver('camera', [0, 0, Math.sign(e.deltaY)]);
        }, { passive: false });

        // Analógicos virtuais de toque continuam no DOM, entregando pela ponte.
        setupVirtualStick('move-stick', 'move-thumb', function (x, y) { deliver('stick', ['move', x, y]); });
        setupVirtualStick('look-stick', 'look-thumb', function (x, y) { deliver('stick', ['look', x, y]); });
        const action = document.getElementById('touch-action');
        if (action) {
            action.addEventListener('pointerdown', function (e) { e.preventDefault(); deliver('inputMode', ['touch']); deliver('action', []); });
        }
        const cancelBtn = document.getElementById('touch-cancel');
        if (cancelBtn) {
            cancelBtn.addEventListener('pointerdown', function (e) { e.preventDefault(); deliver('inputMode', ['touch']); postEventToDotnet('OnClose', []); });
        }
        const resize = function () { deliver('resize', [canvas.clientWidth, canvas.clientHeight]); };
        window.addEventListener('resize', resize);
        // Celular: girar o aparelho ou abrir o teclado muda o visualViewport antes (ou sem) window.resize.
        // A área coberta pelo teclado vira --ultima-vv-bottom para a folha do diálogo subir acima dele.
        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', function () {
                const vv = window.visualViewport;
                const covered = window.innerHeight - vv.height - vv.offsetTop;
                document.documentElement.style.setProperty('--ultima-vv-bottom', (covered > 60 ? Math.round(covered) : 0) + 'px');
                resize();
            });
        }
        window.addEventListener('orientationchange', function () { setTimeout(resize, 250); });
    }

    function setupVirtualStick(hostId, thumbId, set) {
        const host = document.getElementById(hostId);
        const thumb = document.getElementById(thumbId);
        if (!host || !thumb) { return; }
        let pointer = null;
        const reset = function () { pointer = null; set(0, 0); thumb.style.transform = 'translate(0, 0)'; };
        host.addEventListener('pointerdown', function (e) {
            deliver('inputMode', ['touch']);
            pointer = e.pointerId;
            try { host.setPointerCapture(pointer); } catch (err) { /* captura opcional */ }
            e.preventDefault();
            update(e);
        });
        host.addEventListener('pointermove', function (e) { if (e.pointerId === pointer) { update(e); } });
        host.addEventListener('pointerup', function (e) { if (e.pointerId === pointer) { reset(); } });
        host.addEventListener('pointercancel', function (e) { if (e.pointerId === pointer) { reset(); } });
        host.addEventListener('lostpointercapture', function (e) { if (e.pointerId === pointer) { reset(); } });
        window.addEventListener('blur', reset);
        function update(e) {
            const r = host.getBoundingClientRect();
            const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            const radius = Math.min(r.width, r.height) * 0.37;
            let dx = (e.clientX - cx) / radius, dy = (e.clientY - cy) / radius;
            const length = Math.hypot(dx, dy);
            if (length > 1) { dx /= length; dy /= length; }
            const magnitude = Math.hypot(dx, dy);
            const dead = 0.18;
            const scaled = magnitude <= dead ? 0 : Math.min(1, (magnitude - dead) / (1 - dead));
            thumb.style.transform = 'translate(' + (dx * radius) + 'px, ' + (dy * radius) + 'px)';
            const dir = magnitude > 0 ? scaled / magnitude : 0;
            set(dx * dir, -dy * dir);
        }
    }

    // ---------- Contrato público (igual nos dois modos) ----------
    window.game = window.game || {};
    Object.assign(window.game, {
        init(canvasId, dotnetRef, modelsRef) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) { return Promise.reject(new Error('canvas ' + canvasId + ' não encontrado')); }
            dotnet = dotnetRef;
            dotnetModels = modelsRef || null;
            const canWorker = !!canvas.transferControlToOffscreen && typeof Worker !== 'undefined';
            const start = (canWorker && FORCE !== 'main')
                ? startWorker(canvas).then(function (ok) {
                        // No worker o core não tem DOM: o input é capturado aqui.
                        setupInput(canvas);
                        return ok;
                    }).catch(function (err) {
                        console.warn('[motor] worker não subiu, caindo no render local:', err && err.message);
                        // O canvas já foi transferido: recarrega em modo main (uma vez por sessão).
                        if (!sessionStorage.getItem('ultima-worker-fallback')) {
                            sessionStorage.setItem('ultima-worker-fallback', '1');
                            location.replace(location.pathname + '?render=main');
                            return new Promise(function () { /* página vai recarregar */ });
                        }
                        throw err;
                    })
                : startMain(canvas);   // no modo main os handlers de DOM são do próprio core
            return start.then(function () { return true; });
        },
        setFrozen(v) { post('setFrozen', [!!v]); },
        resetPlayer() { post('resetPlayer'); },
        teleportToGuardian() { return query('teleportToGuardian'); },
        addRemote(id, name) { post('addRemote', [id, name]); },
        renameRemote(id, name) { post('renameRemote', [id, name]); },
        updateRemote(id, x, z, ry) { post('updateRemote', [id, x, z, ry]); },
        removeRemote(id) { post('removeRemote', [id]); },
        setMapTiles(tiles) { post('setMapTiles', [tiles]); },
        setMapJobs(jobs) { post('setMapJobs', [jobs]); },
        models: {
            register(json, placeholder) { post('models.register', [json, placeholder]); return true; },
            loaderDone(log) { post('models.loaderDone', [log]); },
            status() { return query('models.status'); },
            get(id) { return query('models.get', [id]); },
            // Variação paramétrica (fase 2): o C# reconstrói o descritor e o mundo troca.
            vary(id, opts) { return query('models.vary', [id, opts]); }
        },
        glb: {
            status() { return query('glb.status'); },
            get(id) { return query('glb.get', [id]); },
            register(id, bytes, opts) { return query('glb.register', [id, bytes, opts]); },
            // Espalhamento determinístico por id (assets do mundo compartilhado, fase 4).
            scatter(id, opts) { return query('glb.scatter', [id, opts]); }
        },
        scenery: {
            // Estilo do mundo (fase 5): preferência da página; o core recebe no boot.
            get() { return sceneryPref(); },
            set(style) {
                const value = style === 'lowpoly' ? 'lowpoly' : 'procedural';
                try { localStorage.setItem(SCENERY_PREF_KEY, value); } catch (e) { /* sem storage */ }
                return value;
            }
        },
        perf: {
            report() { return query('perf.report'); }
        },
        assetsReport() { return query('assetsReport'); },
        coreId() { return query('coreId'); }
    });
    Object.defineProperty(window.game, 'mode', { get: function () { return mode; } });
})();
