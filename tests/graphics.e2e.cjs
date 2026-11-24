const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const main = process.env.ULTIMA_MODE === 'main';

(async () => {
    const browser = await chromium.launch({
        executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)
    });
    try {
        const page = await browser.newPage({ viewport: { width: 960, height: 640 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && /shader|compil|framebuffer/i.test(message.text())) errors.push(message.text());
        });
        const url = new URL(site);
        url.search = 'live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999&gfx=cinema&gpu=0' + (main ? '&render=main' : '');
        await page.goto(url.href);
        if (process.env.ULTIMA_BUILD)
            assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
        for (let attempt = 0; ; attempt++) {
            if (await page.evaluate(async () => {
                const chunks = await window.game?.build?.state?.();
                return chunks?.chunks >= 25;
            })) break;
            if (attempt === 100) throw Error('cena indisponível: ' + errors.join('; '));
            await page.waitForTimeout(250);
        }
        const status = await page.evaluate(() => game.graphicsStatus());
        assert.deepEqual(status, { style: 'cinema', env: true, csm: true, ssao: true, depthOfField: true });
        assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
        const renderer = main ? page : page.workers()[0];
        const detail = await renderer.evaluate(() => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const shadow = scene.getLightByName('sun').getShadowGenerator(scene.activeCamera);
            return { cascades: shadow?.numCascades, fps: scene.getEngine().getFps(), gpu: scene.getEngine().getGlInfo().renderer };
        });
        assert.equal(detail.cascades, 3);
        const countAvatarShadows = () => renderer.evaluate(() => {
            const s = BABYLON.EngineStore.LastCreatedScene;
            const map = s.getLightByName('sun').getShadowGenerator(s.activeCamera).getShadowMap();
            return map.renderList.filter(mesh => mesh.name.includes('remote-shadow-cascade')).length;
        });
        await page.evaluate(() => {
            game.addRemote('shadow-cascade', 'Teste CSM');
            game.updateRemote('shadow-cascade', 1, 3, 0);
        });
        for (let attempt = 0; !(await countAvatarShadows()); attempt++) {
            assert.ok(attempt < 30, 'avatar próximo deve projetar sombra em cascata');
            await page.waitForTimeout(100);
        }
        await page.evaluate(() => game.updateRemote('shadow-cascade', 40, 3, 0));
        for (let attempt = 0; await countAvatarShadows(); attempt++) {
            assert.ok(attempt < 30, 'avatar distante deve sair do mapa de sombras em cascata');
            await page.waitForTimeout(100);
        }
        await page.evaluate(() => game.removeRemote('shadow-cascade'));
        assert.deepEqual(errors, []);
        if (process.env.ULTIMA_CAPTURE) await page.screenshot({ path: process.env.ULTIMA_CAPTURE });
        console.log({ mode: main ? 'main' : 'worker', ...status, ...detail });
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
