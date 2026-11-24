// Compare duas publicações servidas por HTTP. Requer playwright-core e Chromium.
const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');

const builds = process.argv.slice(2);
assert.equal(builds.length, 2, 'Informe as URLs base de referência e candidata');
const software = !process.env.ULTIMA_CHROMIUM_ARGS;
const args = software
    ? ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader']
    : process.env.ULTIMA_CHROMIUM_ARGS.split(' ').filter(Boolean);

async function instrument(route) {
    const response = await route.fetch();
    const source = await response.text();
    const body = source.replaceAll(/terrain\.(AnimateCompanion|UpdateRemoteAvatars)\(/g, 'measureRemoteExport(terrain.$1, ')
        .replace('    function updateOtherCharacters(delta) {', `
    function measureRemoteExport(exported, ...args) {
        const probe = globalThis.__remotePerf;
        if (!probe) { return exported(...args); }
        const start = performance.now();
        const result = exported(...args);
        probe.wasm += performance.now() - start;
        probe.calls++;
        return result;
    }
    function updateOtherCharacters(delta) {`)
        .replace('                updateOtherCharacters(delta);', `
                const probe = globalThis.__remotePerf;
                const remoteStart = probe && performance.now();
                updateOtherCharacters(delta);
                const remoteMs = probe && performance.now() - remoteStart;`)
        .replace('                framePerf(performance.now() - frameStart);', `
                if (probe) {
                    const end = performance.now();
                    probe.rows.push([remoteMs, probe.wasm, probe.calls, end - frameStart, end - probe.previous]);
                    probe.previous = end;
                    probe.wasm = probe.calls = 0;
                }
                framePerf(performance.now() - frameStart);`);
    assert.notEqual(body, source);
    await route.fulfill({ response, body });
}

function summarize(rows, column) {
    const data = rows.map(row => row[column]).sort((a, b) => a - b);
    const rounded = value => Number(value.toFixed(3));
    return {
        mean: rounded(data.reduce((a, b) => a + b, 0) / data.length),
        p95: rounded(data[Math.ceil(data.length * 0.95) - 1])
    };
}

async function collect(page, url) {
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/js/game-core.js*', instrument);
    const root = new URL(url);
    root.search = 'live=0&rede=0&gpu=0&api=http%3A%2F%2F127.0.0.1%3A19999';
    await page.goto(root.href, { waitUntil: 'domcontentloaded' });
    let ready = false;
    for (let i = 0; i < 180 && !ready; i++) {
        ready = await page.evaluate(async () => {
            if (window.game?.mode !== 'worker' || !game.models?.status) { return false; }
            const [models, glb] = await Promise.all([game.models.status(), game.glb.status()]);
            return glb.queued > 0 && glb.loaded.length === glb.queued &&
                models?.queue === 0 && models.loaded.some(s => s.startsWith('character.wanderer'))
                && (await game.build.state()).chunks >= 25;
        });
        if (!ready) { await page.waitForTimeout(250); }
    }
    assert.ok(ready, 'Worker e modelos precisam estar prontos');
    const worker = page.workers()[0];
    const renderer = await worker.evaluate(() => BABYLON.EngineStore.LastCreatedScene.getEngine().getGlInfo().renderer);
    if (software) { assert.match(renderer, /SwiftShader/i); }
    else { assert.doesNotMatch(renderer, /SwiftShader|llvmpipe|software/i); }
    console.log(JSON.stringify({ url, renderer }));
    await worker.evaluate(() => new Promise(resolve => {
        const observable = BABYLON.EngineStore.LastCreatedScene.getEngine().onEndFrameObservable;
        let frames = 0;
        const hook = observable.add(() => {
            if (++frames !== 90) { return; }
            observable.remove(hook);
            resolve();
        });
    }));
    for (const count of [12, 32, 64]) {
        for (const moving of [false, true]) {
            const { rows, remoteShadowCasters, enabledAvatars } = await worker.evaluate(({ count, moving }) => new Promise((resolve, reject) => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const engine = scene.getEngine();
                const update = (i, step) => {
                    const near = i % 2 === 0;
                    const x = (near ? -7 : 34) + (Math.floor(i / 2) % 8) * 1.15;
                    const z = 4 + Math.floor(i / 16) * 1.4;
                    game.updateRemote('perf-' + i, x + (step ? 0.8 : 0), z, step ? 0.8 : 0);
                };
                for (let i = 0; i < count; i++) {
                    game.addRemote('perf-' + i, 'Avatar ' + i);
                    update(i, 0);
                }
                let frame = 0;
                const warmup = 36;
                const samples = 90;
                const timer = setTimeout(() => {
                    engine.onEndFrameObservable.remove(hook);
                    reject(new Error('Amostragem excedeu o tempo limite'));
                }, 90000);
                const hook = engine.onEndFrameObservable.add(() => {
                    frame++;
                    if (moving && frame % 12 === 0) {
                        for (let i = 0; i < count; i++) { update(i, (frame / 12) % 2); }
                    }
                    if (frame === warmup) {
                        globalThis.__remotePerf = { rows: [], wasm: 0, calls: 0, previous: performance.now() };
                    }
                    if (frame !== warmup + samples) { return; }
                    engine.onEndFrameObservable.remove(hook);
                    clearTimeout(timer);
                    const data = globalThis.__remotePerf.rows;
                    globalThis.__remotePerf = null;
                    const sun = scene.getLightByName('sun');
                    const map = (sun.getShadowGenerator(scene.activeCamera) ?? sun.getShadowGenerator()).getShadowMap();
                    const remoteShadowCasters = map.renderList.filter(mesh => mesh.name.includes('remote-perf-')).length;
                    let enabledAvatars = 0;
                    for (let i = 0; i < count; i++) {
                        if (scene.getTransformNodeByName('remote-perf-' + i).isEnabled()) { enabledAvatars++; }
                    }
                    for (let i = 0; i < count; i++) { game.removeRemote('perf-' + i); }
                    resolve({ rows: data, remoteShadowCasters, enabledAvatars });
                });
            }), { count, moving });
            assert.deepEqual(errors, []);
            assert.equal(rows.length, 90);
            assert.ok(rows.every(row => row[2] > 0), 'Instrumentação WASM precisa registrar chamadas');
            console.log(JSON.stringify({ url, count, moving, samples: rows.length,
                companionsMs: summarize(rows, 0), wasmMs: summarize(rows, 1),
                sceneAdapterMs: summarize(rows.map(row => [Math.max(0, row[0] - row[1])]), 0),
                calls: rows[0][2], remoteShadowCasters, enabledAvatars, renderLoopMs: summarize(rows, 3), frameIntervalMs: summarize(rows.slice(1), 4)
            }));
        }
    }
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.ULTIMA_CHROMIUM || undefined, args });
    try {
        for (const url of builds) {
            const page = await browser.newPage({ viewport: { width: 640, height: 360 } });
            try { await collect(page, url); }
            finally { await page.close(); }
        }
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
