const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const counts = (process.env.ULTIMA_COUNTS || '100,300').split(',').map(Number);
const main = process.env.ULTIMA_MODE === 'main';

(async () => {
    const browser = await chromium.launch({
        executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)
    });
    try {
        for (const count of counts) {
            const page = await browser.newPage({ viewport: { width: 1200, height: 720 } });
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            page.on('console', message => {
                if (message.type() === 'error' && /shader|program|vertex.*error/i.test(message.text())) errors.push(message.text());
            });
            const url = new URL(site);
            url.search = new URLSearchParams({
                live: '0', rede: '0', api: 'http://127.0.0.1:19999', crowd: count,
                ...(main ? { render: 'main', gpu: '0' } : {})
            }).toString();
            await page.goto(url.href);
            if (process.env.ULTIMA_BUILD)
                assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
            async function sample(condition) {
                for (let attempt = 0; attempt < 80; attempt++) {
                    const status = await page.evaluate(() => window.game?.crowdStatus?.());
                    if (status?.error) throw new Error(status.error);
                    if (condition(status)) return status;
                    await page.waitForTimeout(250);
                }
                throw new Error('a multidão não atingiu o estado esperado');
            }
            const first = await sample(s => s?.types?.length === 2 && s.types.every(t => t.ready && t.visible[0] > 0));
            assert.equal(first.requested, count);
            assert.ok(first.frustumCulled > 0, 'instâncias atrás da câmera devem sair do desenho');
            assert.ok(first.types.every(t => t.vertices[1] < t.vertices[0]), 'modelo distante deve ter menos vértices');
            assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
            const renderer = main ? page : page.workers()[0];
            // Poucos pixels por personagem exercitam o segundo LOD sem depender da GPU.
            await page.setViewportSize({ width: 720, height: 320 });
            const small = await sample(s => s?.types?.some(t => t.visible[1] > 0) &&
                s.types.every(t => t.ready && t.time > first.types[0].time));
            const shared = await renderer.evaluate(() => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const a = scene.getMeshByName('crowd-high-0'), b = scene.getMeshByName('crowd-low-0');
                const mesh = scene.meshes.find(mesh => mesh.name.startsWith('crowd-') && mesh.isEnabled());
                return {
                    skeleton: a.skeleton === b.skeleton,
                    palette: a.bakedVertexAnimationManager.texture === b.bakedVertexAnimationManager.texture,
                    compiled: !!mesh?.subMeshes[0]?.effect?.isReady(),
                    renderer: scene.getEngine().getGlInfo()?.renderer
                };
            });
            assert.ok(shared.skeleton && shared.palette && shared.compiled);
            await renderer.evaluate(() => {
                BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.x = 120;
            });
            const far = await sample(s => s?.distanceCulled === count);
            assert.ok(far.types.every(type => type.visible.every(value => value === 0)));
            assert.deepEqual(errors, []);
            console.log(JSON.stringify({ count, mode: main ? 'main' : 'worker', baseline: first, small, far: far.distanceCulled, shared }));
            await page.close();
        }
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
