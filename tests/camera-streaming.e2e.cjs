// A câmera compartilha o mesmo trace nos dois renderizadores.
const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const main = process.env.ULTIMA_MODE === 'main';
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';

async function waitFor(check, message) {
    for (let retry = 0; retry < 100; retry++) {
        const value = await check();
        if (value) return value;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(message);
}

(async () => {
    const browser = await chromium.launch({
        executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)
    });
    try {
        const page = await browser.newPage({ viewport: { width: 1200, height: 720 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = new URL(site);
        url.search = new URLSearchParams({
            live: '0', rede: '0', api: 'http://127.0.0.1:19999',
            ...(main ? { render: 'main', gpu: '0' } : {})
        }).toString();
        await page.goto(url.href);
        if (process.env.ULTIMA_BUILD)
            assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
        await waitFor(() => page.evaluate(async () => {
            const glb = await window.game?.glb?.status?.();
            const models = await window.game?.models?.status?.();
            return glb?.loaded.some(id => id.startsWith('flora.pine')) && models?.queue === 0;
        }), 'o cenário não carregou');
        assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
        const renderer = main ? page : page.workers()[0];
        const camera = () => renderer.evaluate(() => {
            const active = BABYLON.EngineStore.LastCreatedScene.activeCamera;
            return { radius: active.radius, alpha: active.alpha };
        });
        const clear = await waitFor(async () => {
            const state = await camera();
            return state.radius > 6 && state;
        }, 'a órbita inicial está bloqueada');
        const trees = [-0.6, 0, 0.6].map((x, i) => ({
            kind: 'place', uid: 'camera-pinheiro-' + i, asset: 'flora.pine', x, z: -2, rot: 0, scale: 2
        }));
        for (const tree of trees) await page.evaluate(op => game.applyWorldOp(op), tree);
        const blocked = await waitFor(async () => {
            const state = await camera();
            return state.radius < 2.4 && state;
        }, 'a câmera atravessou o pinheiro');
        assert.ok(blocked.radius > 0.12);
        for (const tree of trees)
            await page.evaluate(uid => game.applyWorldOp({ kind: 'remove', uid }), tree.uid);
        await waitFor(async () => (await camera()).radius >= clear.radius - 0.05, 'o zoom não voltou ao remover as árvores');

        const canvas = await page.locator('#game-canvas').boundingBox();
        await page.mouse.move(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
        await page.mouse.down();
        await page.mouse.move(canvas.x + canvas.width / 2 + 120, canvas.y + canvas.height / 2, { steps: 4 });
        await page.mouse.up();
        await waitFor(async () => Math.abs((await camera()).alpha - clear.alpha) > 0.2, 'o arrasto não girou a câmera');
        await page.mouse.wheel(0, -360);
        await waitFor(async () => (await camera()).radius < clear.radius - 0.5, 'a roda não aproximou a câmera');

        const setX = x => renderer.evaluate(x => {
            BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.x = x;
        }, x);
        await setX(144);
        const chunks = await waitFor(() => renderer.evaluate(() => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            if (scene.getMeshByName('ground-0,0')) return null;
            const names = scene.meshes.map(mesh => mesh.name).filter(name => /^ground-[-\d]+,[-\d]+$/.test(name));
            return names.length >= 25 && names.includes('ground-5,0') && {
                count: names.length, unique: new Set(names).size, fog: scene.fogDensity
            };
        }), 'a janela não carregou os vizinhos ou manteve o chunk de origem');
        assert.equal(chunks.count, chunks.unique);
        assert.ok(chunks.fog > 0);
        assert.deepEqual(errors, []);
        console.log({ mode: main ? 'main' : 'worker', clear, blocked, chunks });
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
