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
    const qs2 = new URLSearchParams(location.search);
    const FORCE = qs2.get('render');   // 'worker' | 'main' | null
    const GPU_OFF = qs2.get('gpu') === '0';   // pula WebGPU (canvas novo, direto WebGL)

    let dotnet = null;
    let dotnetModels = null;    // ponte de variação paramétrica (ModelsInterop.VaryModel)
    let core = null;            // api do game-core no modo main
    let worker = null;
    let mode = 'main';
    const calls = new Map();    // request id -> resolve (diagnósticos no modo worker)
    let callSeq = 0;

    // Usa a batida de pose existente. A chave separa os mapas servidos por APIs distintas.
    let poseKey, lastSavedPose, latestPose, nextPoseWrite = 0;

    function validPose(pose) {
        return pose && Number.isFinite(pose.x) && Math.abs(pose.x) <= 400 &&
            Number.isFinite(pose.z) && Math.abs(pose.z) <= 400 &&
            Number.isFinite(pose.ry) && Math.abs(pose.ry) <= 2 * Math.PI;
    }

    function restorePose(environment, defaultPose) {
        const defaultEnvironment = !qs2.get('world');
        poseKey = 'ultima.posicao.v1:' + (defaultEnvironment ? '' : environment + ':') + window.game.live.api();
        let saved;
        try { saved = JSON.parse(window.game.browser.read(poseKey)); } catch { /* registro inválido */ }
        lastSavedPose = latestPose = validPose(saved) ? saved : defaultPose;
        return latestPose;
    }

    function savePose() {
        if (!latestPose || latestPose === lastSavedPose) return;
        window.game.browser.write(poseKey, JSON.stringify(latestPose));
        lastSavedPose = latestPose;
        nextPoseWrite = performance.now() + 1000;
    }

    window.addEventListener('pagehide', savePose);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') savePose();
    });

    const SCENERY_PREF_KEY = 'ultima.cenario';
    function sceneryPref() {
        try { return localStorage.getItem(SCENERY_PREF_KEY) === 'lowpoly' ? 'lowpoly' : 'procedural'; }
        catch (e) { return 'procedural'; }
    }

    const GRAPHICS_PREF_KEY = 'ultima.graficos';
    function graphicsStyle(value) {
        return value === 'pbr' || value === 'cinema' ? value : 'padrao';
    }
    function graphicsPref() {
        try { return graphicsStyle(qs2.get('gfx') || localStorage.getItem(GRAPHICS_PREF_KEY)); }
        catch (e) { return graphicsStyle(qs2.get('gfx')); }
    }

    const WEATHER_PREF_KEY = 'ultima.clima';
    const weatherModes = ['clear', 'cloudy', 'fog', 'rain', 'heavy-rain'];
    function weatherMode(value) { return weatherModes.includes(value) ? value : 'clear'; }
    function weatherPref() {
        try { return weatherMode(localStorage.getItem(WEATHER_PREF_KEY)); }
        catch (e) { return 'clear'; }
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
        if (latestPose) {
            const savedX = Math.round(x * 1000) / 1000;
            const savedZ = Math.round(z * 1000) / 1000;
            const angle = Math.abs(ry) > Math.PI + 0.00005 ? Math.atan2(Math.sin(ry), Math.cos(ry)) : ry;
            const savedYaw = Math.round(angle * 10000) / 10000;
            if (savedX !== latestPose.x || savedZ !== latestPose.z || savedYaw !== latestPose.ry) {
                const pose = { x: savedX, z: savedZ, ry: savedYaw };
                if (validPose(pose)) latestPose = pose;
            }
            if (latestPose !== lastSavedPose && performance.now() >= nextPoseWrite) savePose();
        }
        const g = window.game;
        if (g && g.net && g.net.sendPos) { g.net.sendPos(x, z, ry); }
        if (g && g.live && g.live.pose) { g.live.pose(x, z, ry); }
    }

    function onInputMode(kind) {
        if (document.body) { document.body.dataset.input = kind; }
    }

    // Ops do mundo vindas do motor (cliques do modo construção): a oficina vive na main
    // thread e é ela quem aplica/valida no servidor — igual à ponte varyModel.
    function routeWorldOp(op) {
        if (op.action === 'collect') { void game.backpack?.collect(op.resource, op.point); }
        else { postEventToDotnet('OnWorldCommand', [op]); }
    }

    // Sonda de capacidade na thread principal: um canvas DESCARTÁVEL responde se o
    // navegador cria contexto WebGL aqui (o worker/OffscreenCanvas pode falhar mesmo
    // com a main OK — e vice-versa; sem WebGL na main e sem WebGPU, nem começa).
    function webglCapableMainThread() {
        try {
            var c = document.createElement('canvas');
            return !!(window.WebGLRenderingContext &&
                (c.getContext('webgl2') || c.getContext('webgl')));
        } catch (e) { return false; }
    }

    // Motor 3D não subiu (sem WebGPU/WebGL): mensagem clara em vez de página morta.
    function showEngineFailure(err) {
        var div = document.createElement('div');
        div.style.cssText = 'position:fixed;inset:0;z-index:99;background:#0d1422ee;color:#e8ecf3;' +
            'display:flex;align-items:center;justify-content:center;font:15px/1.6 system-ui;padding:2rem;text-align:center';
        div.innerHTML = '<div style="max-width:34rem"><h2 style="margin:0 0 .6rem">Motor 3D não conseguiu iniciar</h2>' +
            '<p>Nem WebGPU nem WebGL estão disponíveis neste navegador/GPU — o Ultima precisa de um deles para renderizar o mundo.</p>' +
            '<p style="opacity:.8">Verifique: <b>chrome://gpu</b> (procure “WebGL/WebGPU: …”); ative a <b>aceleração de hardware</b> nas configurações do navegador; atualize os drivers da placa de vídeo; teste outro navegador (Chrome/Edge/Firefox atuais).</p>' +
            '<p style="opacity:.6;font-size:12px">' + String(err && err.message || err).replace(/[<>]/g, '') + '</p></div>';
        document.body.appendChild(div);
    }

    // Feedback do modo construção: cursor de mira sobre o canvas.
    let worldBuildMode = 'off';
    let refreshWorldHover;
    function onBuildMode(mode) {
        worldBuildMode = mode;
        var canvas = document.getElementById('game-canvas');
        if (canvas) { canvas.style.cursor = mode && mode !== 'off' ? 'crosshair' : ''; }
        refreshWorldHover?.();
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

    function startMain(canvas, spawn, terrainSettings) {
        return loadBabylon().then(function () {
            return new Promise(function (resolve, reject) {
                mode = 'main';
                core = window.UltimaCore.create({
                    canvas: canvas,
                    mode: 'main',
                    build: buildStamp(),
                    pageBase: location.href,
                    scenery: sceneryPref(),
                    graphics: graphicsPref(),
                    gpuOff: GPU_OFF,
                    weather: weatherPref(),
                    spawn,
                    terrainSettings,
                    global: window,
                    dom: true,
                    postEvent: postEventToDotnet,
                    sendPose: sendPose,
                    onInputMode: onInputMode,
                    varyModel: varyModelAsync,
                    worldOp: routeWorldOp,
                    onBuildMode: onBuildMode,
                    onCoreReady: function () { resolve(true); },
                    onCoreError: reject
                });
            });
        });
    }

    // ---------- Modo worker: canvas transferido + ponte de mensagens ----------
    // Tamanho de exibição do canvas (CSS) × densidade de pixels. No worker o
    // OffscreenCanvas não tem layout: o shell mede e envia (init + mudanças).
    function canvasMetrics(canvas) {
        return { width: Math.max(1, canvas.clientWidth || 1280), height: Math.max(1, canvas.clientHeight || 720), dpr: window.devicePixelRatio || 1 };
    }

    function startWorker(canvas, spawn, terrainSettings) {
        const off = canvas.transferControlToOffscreen();
        // Mesma versão dos scripts da página (?v=<build>): sem isso o cache HTTP
        // (10 min) pode entregar um worker/motor antigos junto com o shell novo logo
        // após uma publicação. O worker repassa a query aos próprios imports.
        worker = new Worker('js/game-worker.js?v=' + encodeURIComponent(buildStamp()));
        return new Promise(function (resolve, reject) {
            // O "sinal de vida" só chega depois de baixar o Babylon (≈9 MB do CDN),
            // aquecer o cache local e construir a cena: em rede lenta isso passa de
            // 20 s. O prazo folgado evita recarregar o jogo no modo sem worker à toa.
            const fatal = setTimeout(function () { reject(new Error('worker não deu sinal de vida')); }, 120000);
            worker.onmessage = function (e) { handleWorkerMessage(e.data, resolve, reject, fatal); };
            worker.onerror = function (e) {
                clearTimeout(fatal);
                reject(new Error('worker: ' + (e && e.message || 'erro')));
            };
            worker.postMessage(
                { type: 'init', canvas: off, size: canvasMetrics(canvas), build: buildStamp(), pageBase: location.href, scenery: sceneryPref(), graphics: graphicsPref(), gpuOff: GPU_OFF, weather: weatherPref(), spawn, terrainSettings },
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
        } else if (msg.type === 'worldOp') {
            routeWorldOp(msg.op);
        } else if (msg.type === 'buildMode') {
            onBuildMode(msg.mode);
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
            if (e.code === 'Escape') {
                e.preventDefault();
                if (!e.repeat) { postEventToDotnet('OnClose', []); }
                return;
            }
            if (editable(e.target) || e.isComposing || e.ctrlKey || e.metaKey || e.altKey) { return; }
            if (!gameCodes.includes(e.code) && e.code !== 'KeyE') { return; }
            e.preventDefault();
            if (e.repeat) { return; }
            deliver('inputMode', ['keyboard']);
            if (e.code === 'KeyE') { deliver('action', []); }
            else { deliver('key', [e.code, true]); }
        });
        window.addEventListener('keyup', function (e) {
            if (!gameCodes.includes(e.code)) { return; }
            deliver('key', [e.code, false]);
            if (!editable(e.target) && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); }
        });

        let blurred = false;
        const padValues = [0, 0, 0, 0, 0, 0];
        function clear() {
            padValues.fill(0);
            deliver('clear', []);
        }
        window.addEventListener('blur', function () { blurred = true; clear(); });
        window.addEventListener('focus', function () { blurred = false; });
        document.addEventListener('visibilitychange', function () {
            if (document.hidden) { clear(); }
        });
        document.addEventListener('focusin', function (e) {
            if (editable(e.target)) { clear(); }
        });

        let actionWasDown = false, cancelWasDown = false;
        let padIndex = null;
        function samplePad(x, y, lookX, lookY, dpadX, dpadY) {
            if (x === padValues[0] && y === padValues[1] && lookX === padValues[2] &&
                lookY === padValues[3] && dpadX === padValues[4] && dpadY === padValues[5]) { return; }
            padValues[0] = x;
            padValues[1] = y;
            padValues[2] = lookX;
            padValues[3] = lookY;
            padValues[4] = dpadX;
            padValues[5] = dpadY;
            deliver('pad', padValues);
        }
        function pollPad() {
            window.requestAnimationFrame(pollPad);
            let pad;
            try {
                const pads = navigator.getGamepads?.();
                if (pads) {
                    for (const candidate of pads) {
                        if (candidate?.connected) { pad = candidate; break; }
                    }
                }
            } catch { /* Gamepad API indisponível */ }
            const nextIndex = pad?.index ?? null;
            if (nextIndex !== padIndex) {
                if (padIndex !== null) {
                    samplePad(0, 0, 0, 0, 0, 0);
                    deliver('padDisconnected', []);
                }
                padIndex = nextIndex;
                actionWasDown = cancelWasDown = false;
            }
            if (!pad) { return; }
            const buttons = pad.buttons;
            const action = !!buttons[0]?.pressed;
            const cancel = !!(buttons[1]?.pressed || buttons[9]?.pressed);
            if (!blurred && !document.hidden) {
                if (!editable(document.activeElement)) {
                    const axes = pad.axes;
                    samplePad(axes[0] || 0, axes[1] || 0, axes[2] || 0, axes[3] || 0,
                        Number(!!buttons[15]?.pressed) - Number(!!buttons[14]?.pressed),
                        Number(!!buttons[12]?.pressed) - Number(!!buttons[13]?.pressed));
                    if (action && !actionWasDown) {
                        deliver('inputMode', ['gamepad']);
                        deliver('action', []);
                    }
                }
                if (cancel && !cancelWasDown) {
                    deliver('inputMode', ['gamepad']);
                    postEventToDotnet('OnClose', []);
                }
            }
            actionWasDown = action;
            cancelWasDown = cancel;
        }
        window.requestAnimationFrame(pollPad);

        setupWorldPointer(canvas);

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

    let animationAudio = null;
    function unlockAnimationAudio() {
        if (worldBuildMode !== 'collect' || !window.AudioContext) { return; }
        try {
            animationAudio ||= new AudioContext();
            if (animationAudio.state === 'suspended') { animationAudio.resume().catch(() => {}); }
        } catch { /* o efeito sonoro é opcional */ }
    }

    function animationSound(kind) {
        if (animationAudio?.state !== 'running' || !['dig', 'gather'].includes(kind)) { return; }
        const hit = kind === 'dig';
        const length = Math.floor(animationAudio.sampleRate * (hit ? 0.10 : 0.07));
        const buffer = animationAudio.createBuffer(1, length, animationAudio.sampleRate);
        const data = buffer.getChannelData(0);
        let previous = 0;
        for (let i = 0; i < length; i++) {
            previous += (Math.random() * 2 - 1 - previous) * (hit ? 0.28 : 0.68);
            data[i] = previous * Math.pow(1 - i / length, 2) * 0.22;
        }
        const source = animationAudio.createBufferSource();
        source.buffer = buffer;
        source.connect(animationAudio.destination);
        source.start();
    }

    function setupWorldPointer(canvas) {
        // Clique e câmera usam a mesma ponte nos dois renderizadores.
        // Um dedo/mouse arrasta e gira; dois dedos são pinça (zoom) e o giro fica
        // suspenso. Convenção do attachControl do Babylon (pinchInwards): juntar os
        // dedos aproxima. A pinça usa a mesma escala da roda: reduzir a distância
        // à metade equivale a ≈7 entalhes. Um clique curto de um dedo (sem arrasto,
        // sem pinça) segue sendo candidata a edição do mundo no modo construção.
        const pointers = new Map();
        let hoverPosition = null;
        let hoverFrame = 0;
        function pointOnCanvas(e) {
            const rect = canvas.getBoundingClientRect();
            // scene.pick aplica a escala do renderizador às coordenadas CSS.
            return [e.clientX - rect.left, e.clientY - rect.top];
        }
        function clearHover() {
            if (!hoverPosition) { return; }
            hoverPosition = null;
            if (worldBuildMode === 'collect') { deliver('terrainHover', [null]); }
        }
        refreshWorldHover = function () {
            if (hoverFrame || !hoverPosition || worldBuildMode !== 'collect') { return; }
            hoverFrame = requestAnimationFrame(function () {
                hoverFrame = 0;
                if (worldBuildMode === 'collect' && hoverPosition) { deliver('terrainHover', [hoverPosition]); }
            });
        };
        canvas.addEventListener('pointermove', function (e) {
            if (e.pointerType !== 'mouse') { return; }
            hoverPosition = pointOnCanvas(e);
            refreshWorldHover();
        });
        canvas.addEventListener('pointerleave', clearHover);
        window.addEventListener('blur', clearHover);
        let lastX = 0, lastY = 0, pinchDist = 0, downAt = 0, downX = 0, downY = 0, pinched = false;
        canvas.addEventListener('pointerdown', function (e) {
            if (e.button !== 0) { return; }
            unlockAnimationAudio();
            pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (pointers.size === 2) {
                const pts = Array.from(pointers.values());
                pinchDist = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
                pinched = true;
            } else {
                lastX = e.clientX; lastY = e.clientY;
                downAt = Date.now(); downX = e.clientX; downY = e.clientY; pinched = false;
            }
        });
        function solta(e) {
            if (!pointers.delete(e.pointerId)) { return; }
            if (pointers.size < 2) { pinchDist = 0; }
            // Clique curto sem arrasto: candidata a edição do mundo (o core ignora fora do modo).
            if (e.type === 'pointerup' && pointers.size === 0 && !pinched && Date.now() - downAt < 500 && Math.hypot(e.clientX - downX, e.clientY - downY) < 6) {
                deliver('terrainClick', pointOnCanvas(e));
            }
        }
        window.addEventListener('pointerup', solta);
        window.addEventListener('pointercancel', solta);
        window.addEventListener('pointermove', function (e) {
            if (!pointers.has(e.pointerId)) { return; }
            if (pointers.size === 2) {
                pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
                const pts = Array.from(pointers.values());
                const d = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
                if (pinchDist > 0 && d > 0) {
                    deliver('camera', [0, 0, 120 * ((d - pinchDist) / pinchDist) * 10]);
                }
                pinchDist = d;
                return;
            }
            deliver('camera', [e.clientX - lastX, e.clientY - lastY, 0]);
            lastX = e.clientX; lastY = e.clientY;
        });
        canvas.addEventListener('wheel', function (e) {
            e.preventDefault();
            // deltaY completo (a magnitude importa no zoom) + deltaMode (linhas ×40).
            deliver('camera', [0, 0, e.deltaY, e.deltaMode]);
        }, { passive: false });

    }

    // No modo main o Babylon só redimensiona no 'resize' da janela; mudanças de layout
    // (painéis, barra de endereço do celular) também alteram o canvas — observa e
    // redimensiona, como no worker.
    function watchSizeMain(canvas) {
        if (typeof ResizeObserver === 'undefined') { return; }
        let last = '';
        new ResizeObserver(function () {
            const key = canvas.clientWidth + 'x' + canvas.clientHeight;
            if (key === last) { return; }
            last = key;
            if (core && core.input) { core.input.resize(canvas.clientWidth, canvas.clientHeight, window.devicePixelRatio || 1); }
        }).observe(canvas);
    }

    // Acompanha o tamanho de exibição: ResizeObserver pega mudanças de layout que não
    // são resize de janela (painéis, HUD); matchMedia pega zoom e troca de monitor.
    function watchSize(canvas) {
        let last = '';
        let mq = null;
        function push() {
            if (!worker) { return; }
            const m = canvasMetrics(canvas);
            const key = m.width + 'x' + m.height + '@' + m.dpr;
            if (key === last) { return; }
            last = key;
            worker.postMessage({ type: 'size', width: m.width, height: m.height, dpr: m.dpr });
        }
        function watchDpr() {
            if (mq && mq.removeEventListener) { mq.removeEventListener('change', watchDpr); }
            try { mq = window.matchMedia('(resolution: ' + (window.devicePixelRatio || 1) + 'dppx)'); } catch (e) { mq = null; return; }
            if (mq.addEventListener) { mq.addEventListener('change', watchDpr); } else if (mq.addListener) { mq.addListener(watchDpr); }
        }
        if (typeof ResizeObserver !== 'undefined') { new ResizeObserver(push).observe(canvas); }
        window.addEventListener('resize', push);
        watchDpr();
        push();
    }

    function setupVirtualStick(hostId, thumbId, set) {
        const host = document.getElementById(hostId);
        const thumb = document.getElementById(thumbId);
        if (!host || !thumb) { return; }
        let pointer = null;
        const reset = function () { pointer = null; set(0, 0); thumb.style.transform = 'translate(0, 0)'; };
        host.addEventListener('pointerdown', function (e) {
            if (pointer !== null || e.button !== 0) { return; }
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
        document.addEventListener('visibilitychange', () => { if (document.hidden) { reset(); } });
        function update(e) {
            const r = host.getBoundingClientRect();
            const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            const radius = Math.min(r.width, r.height) * 0.37;
            let dx = (e.clientX - cx) / radius, dy = (e.clientY - cy) / radius;
            const length = Math.hypot(dx, dy);
            if (length > 1) { dx /= length; dy /= length; }
            thumb.style.transform = 'translate(' + (dx * radius) + 'px, ' + (dy * radius) + 'px)';
            set(dx, -dy);
        }
    }

    // ---------- Contrato público (igual nos dois modos) ----------
    window.game = window.game || {};
    Object.assign(window.game, {
        loadBabylon,
        buildStamp,
        init(canvasId, dotnetRef, modelsRef, environment, terrainSettings) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) { return Promise.reject(new Error('canvas ' + canvasId + ' não encontrado')); }
            dotnet = dotnetRef;
            dotnetModels = modelsRef || null;
            // Sem WebGL na main E sem sinal de WebGPU: nem tenta — o diagnóstico é da
            // máquina (driver/aceleração), o overlay diz exatamente o que verificar.
            const gpuSignal = typeof navigator !== 'undefined' && !!navigator.gpu;
            if (!webglCapableMainThread() && !gpuSignal) {
                var err = new Error('WebGL não disponível neste navegador (verifique chrome://gpu e a aceleração de hardware).');
                console.error('[motor]', err.message);
                showEngineFailure(err);
                return Promise.resolve(false);
            }
            const spawn = restorePose(environment, terrainSettings.spawn);
            const canWorker = !!canvas.transferControlToOffscreen && typeof Worker !== 'undefined';
            const start = (canWorker && FORCE !== 'main')
                ? startWorker(canvas, spawn, terrainSettings).then(function (ok) {
                        // No worker o core não tem DOM: o input é capturado aqui.
                        setupInput(canvas);
                        watchSize(canvas);   // resolução de render casada com a tela (CSS × dpr)
                        return ok;
                    }).catch(function (err) {
                        console.warn('[motor] worker não subiu, caindo no render local:', err && err.message);
                        // O canvas já foi transferido: recarrega em modo main (uma vez por sessão).
                        if (!sessionStorage.getItem('ultima-worker-fallback')) {
                            sessionStorage.setItem('ultima-worker-fallback', '1');
                            // gpu=0: o canvas desta página pode ter ficado presa numa
                            // tentativa WebGPU — a próxima carga vai direto pro WebGL.
                            const fallback = new URL(location.href);
                            fallback.searchParams.set('render', 'main');
                            fallback.searchParams.set('gpu', '0');
                            location.replace(fallback.href);
                            return new Promise(function () { /* página vai recarregar */ });
                        }
                        throw err;
                    })
                : startMain(canvas, spawn, terrainSettings).then(function (ok) {
                        setupInput(canvas);
                        watchSizeMain(canvas);
                        return ok;
                    });
            return start.then(function () { return true; });
        },
        setFrozen(v, speaker) { post('setFrozen', [!!v, speaker]); },
        animationSound,
        characterAction(name, point) { post('characterAction', [name, point]); },
        characterDialogueLine(speaker, length) { post('characterDialogueLine', [speaker, length]); },
        resetPlayer() { post('resetPlayer'); },
        crowdStatus() { return query('crowdStatus'); },
        graphicsStatus() { return query('graphicsStatus'); },
        teleportToGuardian() { return query('teleportToGuardian'); },
        addRemote(id, name) { post('addRemote', [id, name]); },
        renameRemote(id, name) { post('renameRemote', [id, name]); },
        updateRemote(id, x, z, ry) { post('updateRemote', [id, x, z, ry]); },
        removeRemote(id) { post('removeRemote', [id]); },
        setMapTiles(tiles) { post('setMapTiles', [tiles]); },
        setMapJobs(jobs) { post('setMapJobs', [jobs]); },
        setWorldEdits(edits, revision) { post('setWorldEdits', [edits, revision]); },
        setPlacements(list) { post('setPlacements', [list]); },
        applyWorldOp(op) { post('applyWorldOp', [op]); },
        setCollectedResources(entries) { post('setCollectedResources', [entries]); },
        build: {
            mode(m, opts) { return query('build.mode', [m, opts]); },
            state() { return query('build.state'); }
        },
        // Clique no terreno pelo caminho interno (automação/teste do modo construção).
        terrainClick(sx, sy) { post('input.terrainClick', [sx, sy]); },
        camBias(dx, dy, wheel) { post('input.camera', [dx, dy, wheel]); },
        keyInput(code, down) { post('input.key', [code, down]); },
        heightAt(x, z) { return query('heightAt', [x, z]); },
        probeMesh(x, z) { return query('probeMesh', [x, z]); },
        debugChunk(x, z) { return query('debugChunk', [x, z]); },
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
        graphics: {
            // F7b: PBR+IBL (materiais glTF originais) ou padrão (StandardMaterial).
            get() { return graphicsPref(); },
            set(style) {
                const value = graphicsStyle(style);
                try { localStorage.setItem(GRAPHICS_PREF_KEY, value); } catch (e) { /* sem storage */ }
                return value;
            }
        },
        weather: {
            get() { return weatherPref(); },
            set(mode) {
                const value = weatherMode(mode);
                try { localStorage.setItem(WEATHER_PREF_KEY, value); } catch (e) { /* sem storage */ }
                post('setWeather', [value]);
                return value;
            },
            status() { return query('weatherStatus'); }
        },
        syncWorldTime(epoch) { post('syncWorldTime', [epoch]); },
        skyStatus() { return query('skyStatus'); },
        perf: {
            report() { return query('perf.report'); }
        },
        assetsReport() { return query('assetsReport'); },
        coreId() { return query('coreId'); },
        renderSize() { return query('renderSize'); },
        cameraState() { return query('cameraState'); },
        playerState() { return query('playerState'); }
    });
    Object.defineProperty(window.game, 'mode', { get: function () { return mode; } });
})();
