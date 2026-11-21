// Protótipo (branch worker-proto): engine Babylon.js rodando num Web Worker com
// canvas transferido (OffscreenCanvas). Objetivo do checkpoint 1 do
// PLAN-WORKER-OFFSCREEN-CANVAS: provar que a engine sobe no worker, medir o custo
// do bundle duplicado (importScripts do mesmo CDN = cache HTTP, mas parse/compile
// e memória extras) e comparar o FPS com o caminho da main thread.
// Mensagens: {type:'init', canvas, width, height} · {type:'resize', width, height}
// · {type:'dispose'} — responde {type:'ready', renderer}, {type:'fps', fps} (2 Hz).
importScripts('https://cdn.jsdelivr.net/npm/babylonjs@9.36.0/babylon.js');

let engine = null;
let scene = null;

self.onmessage = function (e) {
    const msg = e.data || {};
    if (msg.type === 'init') {
        try {
            const t0 = performance.now();
            engine = new BABYLON.Engine(msg.canvas, true, { adaptToDeviceRatio: true, stencil: false });
            scene = new BABYLON.Scene(engine);
            scene.clearColor = new BABYLON.Color4(0.1, 0.12, 0.16, 1);

            const hemi = new BABYLON.HemisphericLight('hemi', new BABYLON.Vector3(0.2, 1, 0.1), scene);
            hemi.intensity = 0.9;
            const sun = new BABYLON.DirectionalLight('sun', new BABYLON.Vector3(-0.7, -0.8, -0.3).normalize(), scene);
            sun.intensity = 1.4;

            const ground = BABYLON.MeshBuilder.CreateGround('ground', { width: 40, height: 40, subdivisions: 96 }, scene);
            ground.receiveShadows = true;
            const sphere = BABYLON.MeshBuilder.CreateSphere('sphere', { diameter: 1.8, segments: 24 }, scene);
            sphere.position.y = 1.2;

            // DynamicTexture no worker: precisa funcionar via OffscreenCanvas 2D.
            let dynamicOk = false;
            try {
                const tex = new BABYLON.DynamicTexture('tex', { width: 64, height: 64 }, scene, false);
                const ctx = tex.getContext();
                ctx.fillStyle = '#d0a020';
                ctx.fillRect(0, 0, 64, 64);
                tex.update(false);
                const mat = new BABYLON.StandardMaterial('mat', scene);
                mat.diffuseTexture = tex;
                sphere.material = mat;
                dynamicOk = true;
            } catch (err) {
                console.warn('[worker-proto] DynamicTexture falhou:', err && err.message);
            }

            const camera = new BABYLON.ArcRotateCamera('camera', -Math.PI / 2, 1.1, 9, new BABYLON.Vector3(0, 1, 0), scene);
            camera.attachControl = null; // input chega por mensagem; sem controle direto no worker

            let frames = 0, acc = 0, last = performance.now();
            engine.runRenderLoop(function () {
                sphere.rotation.y += 0.02;
                scene.render();
                frames++;
                const now = performance.now();
                acc += now - last;
                last = now;
                if (acc >= 500) {
                    self.postMessage({ type: 'fps', fps: Math.round(frames / (acc / 1000)) });
                    frames = 0; acc = 0;
                }
            });

            let renderer = 'WebGL';
            try {
                const glInfo = engine.getGlInfo();
                if (glInfo && (glInfo.renderer || glInfo.vendor)) { renderer = glInfo.renderer || glInfo.vendor; }
            } catch (err) { /* identificação não suportada */ }
            self.postMessage({ type: 'ready', renderer: renderer, dynamicTexture: dynamicOk, initMs: Math.round(performance.now() - t0) });
        } catch (err) {
            self.postMessage({ type: 'error', message: String(err && err.message || err) });
        }
    } else if (msg.type === 'resize' && engine) {
        engine.setSize(msg.width, msg.height);
    } else if (msg.type === 'dispose' && engine) {
        engine.stopRenderLoop();
        scene.dispose();
        engine.dispose();
        engine = null; scene = null;
    }
};
