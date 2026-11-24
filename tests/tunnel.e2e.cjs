const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const main = process.env.ULTIMA_MODE === 'main';

async function waitFor(check, message, attempts = 100) {
    for (let retry = 0; retry < attempts; retry++) {
        const result = await check();
        if (result) return result;
        await new Promise(resolve => setTimeout(resolve, 200));
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
        url.search = 'live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999' + (main ? '&render=main&gpu=0' : '');
        await page.goto(url.href);
        if (process.env.ULTIMA_BUILD)
            assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
        await waitFor(() => page.evaluate(async () => (await window.game?.build?.state?.())?.chunks >= 25),
            'o terreno não carregou');
        assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
        const renderer = main ? page : page.workers()[0];
        const tunnel = { kind: 'tunnel', x: 5, z: 11, r: 12, radius: 2.4, rev: 1,
            points: [{ x: 0, y: 0.8, z: 4 }, { x: 4, y: -3.2, z: 10 }, { x: 10, y: 1.1, z: 18 }] };
        await page.evaluate(op => game.setWorldEdits([op], 1), tunnel);
        const chunk = await waitFor(async () => {
            const result = await page.evaluate(() => game.debugChunk(0, 0));
            return result?.volume && result;
        }, 'a malha volumétrica não chegou ao Babylon');
        assert.ok(chunk.totalVertices > 4225);
        const pose = () => renderer.evaluate(() => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const { x, y, z } = scene.getTransformNodeByName('player').position;
            return { x, y, z, radius: scene.activeCamera.radius };
        });
        const setPose = y => renderer.evaluate(y => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            scene.getTransformNodeByName('player').position.set(4, y, 10);
        }, y);
        async function walkUntil(keys, reached) {
            await page.locator('#game-canvas').focus();
            for (const key of keys) await page.keyboard.down(key);
            try {
                return await waitFor(async () => {
                    const state = await pose();
                    return reached(state) && state;
                }, 'o jogador não avançou durante a caminhada', 15);
            } finally {
                for (const key of keys) await page.keyboard.up(key);
            }
        }
        const above = await page.evaluate(() => game.heightAt(4, 10));
        await setPose(above + 0.1);
        await waitFor(async () => Math.abs((await pose()).y - above) < 0.13, 'o teto perdeu o piso externo');
        const onTop = await walkUntil(['w'], state => state.z > 10.5);
        assert.ok(onTop.y > -0.2, JSON.stringify(onTop));
        await page.evaluate(() => game.resetPlayer());
        await setPose(-4.7);
        const inside = await waitFor(async () => {
            const state = await pose();
            return state.y < -4.4 && state.radius < 6 && state;
        }, 'o jogador ou a câmera atravessou o teto');
        const walked = await walkUntil(['w', 'd'],
            state => Math.hypot(state.x - inside.x, state.z - inside.z) > 0.5);
        assert.ok(walked.y < above - 1);
        await page.evaluate(() => game.setWorldEdits([], 2));
        await waitFor(async () => (await page.evaluate(() => game.debugChunk(0, 0))).volume === false,
            'o replay não restaurou a malha da superfície');
        assert.deepEqual(errors, []);
        console.log({ mode: main ? 'main' : 'worker', above, inside, walked, vertices: chunk.totalVertices });
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
