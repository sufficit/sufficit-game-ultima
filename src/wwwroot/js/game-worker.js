// Ultima — motor no Web Worker: Babylon + assets + game-core importados aqui, canvas
// veio transferido da main thread (OffscreenCanvas). A ponte de mensagens está
// documentada no game.js (shell): init/call/input de lá para cá; ready/fatal/event/
// pose/inputMode/result de cá para lá.
// O shell cria este worker com ?v=<build>; os imports relativos herdam a query para
// ficarem na MESMA versão dos scripts da página (e reaproveitar a entrada de cache).
const V = location.search;
importScripts('https://cdn.jsdelivr.net/npm/babylonjs@9.36.0/babylon.js');
importScripts('assets.js' + V);
importScripts('game-crowd.js' + V);
importScripts('game-core.js' + V);
const terrainRuntime = import('../terrain-wasm/host.js' + V).then(module => module.ready);
terrainRuntime.catch(() => {}); // A inicialização do core encaminha a falha ao shell.

let core = null;

// Variação paramétrica pedida pelo core: vai à main thread (ModelsInterop no .NET)
// e volta como descritor JSON — pending por request id, como os diagnósticos do shell.
let varySeq = 0;
const varyPending = new Map();
function varyModelViaShell(modelId, seed, detail) {
    return new Promise(function (resolve, reject) {
        const reqId = ++varySeq;
        varyPending.set(reqId, { resolve: resolve, reject: reject });
        self.postMessage({ type: 'varyModel', reqId: reqId, modelId: modelId, seed: seed, detail: detail });
    });
}

// O bootstrap do .NET usa self.onmessage para distinguir seu worker auxiliar.
self.addEventListener('message', function (e) {
    const msg = e.data || {};
    if (msg.type === 'varyResult') {
        const p = varyPending.get(msg.reqId);
        if (p) {
            varyPending.delete(msg.reqId);
            if (msg.error) { p.reject(new Error(msg.error)); } else { p.resolve(msg.json); }
        }
        return;
    }
    if (msg.type === 'init') {
        try {
            core = self.UltimaCore.create({
                canvas: msg.canvas,
                mode: 'worker',
                build: msg.build,
                pageBase: msg.pageBase,
                scenery: msg.scenery,
                graphics: msg.graphics,
                weather: msg.weather,
                size: msg.size || null,
                terrainRuntime,
                global: self,
                dom: false,
                postEvent: function (name, args) { self.postMessage({ type: 'event', name: name, args: args }); },
                sendPose: function (x, z, ry) { self.postMessage({ type: 'pose', x: x, z: z, ry: ry }); },
                onInputMode: function (kind) { self.postMessage({ type: 'inputMode', kind: kind }); },
                worldOp: function (op) { self.postMessage({ type: 'worldOp', op: op }); },
                onBuildMode: function (mode) { self.postMessage({ type: 'buildMode', mode: mode }); },
                varyModel: varyModelViaShell,
                onCoreReady: function () { self.postMessage({ type: 'ready' }); },
                onCoreError: function (message) { self.postMessage({ type: 'fatal', message: message }); }
            });
        } catch (err) {
            self.postMessage({ type: 'fatal', message: String(err && err.message || err) });
        }
        return;
    }
    if (!core) { return; }
    if (msg.type === 'size') {
        // Tamanho de exibição medido pelo shell (CSS × dpr): mantém a resolução de
        // render casada com a tela em redimensionamento, zoom e troca de monitor.
        core.input.resize(msg.width, msg.height, msg.dpr);
        return;
    }
    if (msg.type === 'input' && msg.method && core.input[msg.method]) {
        core.input[msg.method].apply(null, msg.args || []);
    } else if (msg.type === 'call' && msg.method) {
        const parts = msg.method.split('.');
        let obj = core;
        for (const part of parts.slice(0, -1)) { obj = obj && obj[part]; }
        const fn = obj && obj[parts[parts.length - 1]];
        if (typeof fn !== 'function') { return; }
        try {
            const value = fn.apply(obj, msg.args || []);
            if (msg.id !== undefined) {
                // Métodos com resposta (status/report/get): resolve a Promise do shell.
                Promise.resolve(value).then(function (v) {
                    self.postMessage({ type: 'result', id: msg.id, value: v === undefined ? null : JSON.parse(JSON.stringify(v)) });
                }, function (err) {
                    self.postMessage({ type: 'result', id: msg.id, value: { error: String(err && err.message || err) } });
                });
            }
        } catch (err) {
            if (msg.id !== undefined) { self.postMessage({ type: 'result', id: msg.id, value: { error: String(err && err.message || err) } }); }
            console.warn('[worker] chamada falhou:', msg.method, err);
        }
    }
});
