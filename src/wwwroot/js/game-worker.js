// Ultima — motor no Web Worker: Babylon + assets + game-core importados aqui, canvas
// veio transferido da main thread (OffscreenCanvas). A ponte de mensagens está
// documentada no game.js (shell): init/call/input de lá para cá; ready/fatal/event/
// pose/inputMode/result de cá para lá.
importScripts('https://cdn.jsdelivr.net/npm/babylonjs@9.36.0/babylon.js');
importScripts('assets.js');
importScripts('game-core.js');

let core = null;

self.onmessage = function (e) {
    const msg = e.data || {};
    if (msg.type === 'init') {
        try {
            core = self.UltimaCore.create({
                canvas: msg.canvas,
                mode: 'worker',
                build: msg.build,
                pageBase: msg.pageBase,
                global: self,
                dom: false,
                postEvent: function (name, args) { self.postMessage({ type: 'event', name: name, args: args }); },
                sendPose: function (x, z, ry) { self.postMessage({ type: 'pose', x: x, z: z, ry: ry }); },
                onInputMode: function (kind) { self.postMessage({ type: 'inputMode', kind: kind }); },
                onCoreReady: function () { self.postMessage({ type: 'ready' }); },
                onCoreError: function (message) { self.postMessage({ type: 'fatal', message: message }); }
            });
        } catch (err) {
            self.postMessage({ type: 'fatal', message: String(err && err.message || err) });
        }
        return;
    }
    if (!core) { return; }
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
};
