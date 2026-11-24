// Reproduz uma interferência no pé em balanço sem encostar os torsos.
const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const main = process.env.ULTIMA_MODE === 'main';
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';

async function instrument(route) {
    const response = await route.fetch();
    const text = await response.text();
    const entry = '    function syncCharacterHitboxes() {';
    const accept = '                terrain.OnFootContact(Math.abs(forward - shortened));';
    assert.ok(text.includes(entry) && text.includes(accept), 'pontos de observação da passada disponíveis');
    const body = text.replace(entry, entry + `
        const fixture = globalThis.__stepFixture;
        if (fixture && !fixture.prepared) {
            fixture.prepared = true;
            const rig = characterRigs.get(player);
            const leg = rig.legs[1];
            const obstacle = remotes.get('step-contact-test').rig.legs[0].shoe;
            const { x, y, z } = player.position;
            fixture.start = { x, z };
            leg.stride.set([0.38, 0, 0.065, 0, 0]);
            const pose = terrain.SolveShortenedFoot(y, terrainHeight(x,z), 0.38, 0, 0.065, 0);
            applyLeg(leg, pose, 0, pose[3], 0);
            leg.shoe.computeWorldMatrix(true);
            const parentInverse = obstacle.parent.computeWorldMatrix(true).clone().invert();
            BABYLON.Vector3.TransformCoordinatesToRef(leg.shoe.getAbsolutePosition(), parentInverse, obstacle.position);
        }`).replace(accept, accept + `
                const observed = globalThis.__stepFixture;
                if (observed && !observed.result) observed.result = { forward, shortened };`);
    await route.fulfill({ response, body });
}

async function until(check, message) {
    for (let attempt = 0; attempt < 120; attempt++) {
        const result = await check();
        if (result) return result;
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw Error(message);
}

(async () => {
    const browser = await chromium.launch({
        executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)
    });
    try {
        const page = await browser.newPage({ viewport: { width: 960, height: 540 } });
        await page.route('**/js/game-core.js*', instrument);
        const url = new URL(site);
        url.search = 'live=0&rede=0&gpu=0&api=http%3A%2F%2F127.0.0.1%3A19999' + (main ? '&render=main' : '');
        await page.goto(url.href);
        if (process.env.ULTIMA_BUILD)
            assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
        await until(() => page.evaluate(async () => {
            const model = await window.game?.models?.status?.();
            return model?.loaded.includes('character.villager v2') &&
                model?.loaded.some(name => name.startsWith('character.wanderer')) && model.queue === 0;
        }), 'modelos humanoides prontos');
        assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
        const renderer = main ? page : page.workers()[0];
        await page.evaluate(() => {
            game.addRemote('step-contact-test', 'Contato do pé');
            game.updateRemote('step-contact-test', 0.95, 0, 0);
        });
        await until(() => renderer.evaluate(() => BABYLON.EngineStore.LastCreatedScene
            .getTransformNodeByName('remote-step-contact-test')?.position.x === 0.95), 'avatar lateral pronto');
        const result = await renderer.evaluate(() => new Promise((resolve, reject) => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const player = scene.getTransformNodeByName('player');
            globalThis.__stepFixture = {};
            scene.onAfterRenderObservable.addOnce(() => {
                const { start, result } = globalThis.__stepFixture;
                delete globalThis.__stepFixture;
                if (!result) { reject(Error('o pé não adotou um passo curto')); return; }
                resolve({ ...result, drift: Math.hypot(player.position.x - start.x, player.position.z - start.z) });
            });
        }));
        assert.ok(result.forward - result.shortened > 0.12 && result.drift < 0.005, JSON.stringify(result));
        console.log(JSON.stringify({ mode: main ? 'main' : 'worker', ...result }));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
