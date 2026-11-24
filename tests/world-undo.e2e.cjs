// Use com um backend Ultima Release e dataDir descartável.
const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const site = process.env.ULTIMA_SITE;
const api = process.env.ULTIMA_API;
assert.ok(site && api && /^http:\/\/127\.0\.0\.1:\d+$/.test(api), 'o teste precisa de um backend descartável local');

async function waitFor(check, message) {
    for (let retry = 0; retry < 100; retry++) {
        if (await check()) return;
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error(message);
}

(async () => {
    const browser = await chromium.launch({
        executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)
    });
    try {
        const url = new URL(site);
        url.search = new URLSearchParams({ rede: '0', api });
        const owner = await browser.newPage({ viewport: { width: 1200, height: 720 } });
        const witness = await browser.newPage({ viewport: { width: 1200, height: 720 } });
        await Promise.all([owner, witness].map(page => page.goto(url.href)));
        const errors = [];
        for (const page of [owner, witness]) page.on('pageerror', error => errors.push(error.message));
        await Promise.all([owner, witness].map(async page => {
            await page.waitForFunction(() => game?.mode === 'worker' && game?.live?.token());
            await waitFor(() => page.evaluate(async () => (await game.build.state()).chunks >= 25), 'chunks não chegaram');
        }));
        async function probe(page) {
            return page.workers()[0].evaluate(() => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const v = new BABYLON.Vector3(0, 0.05, 1);
                const hit = scene.pickWithRay(new BABYLON.Ray(new BABYLON.Vector3(v.x, 100, v.z), new BABYLON.Vector3(0,-1,0)),
                    mesh => mesh.name === 'ground-0,0');
                const point = BABYLON.Vector3.Project(v, BABYLON.Matrix.Identity(), scene.getTransformMatrix(),
                    scene.activeCamera.viewport.toGlobal(scene.getEngine().getRenderWidth(), scene.getEngine().getRenderHeight()));
                return { x: point.x, y: point.y, height: hit.pickedPoint.y };
            });
        }
        const before = await probe(owner);
        assert.ok(Math.abs((await probe(witness)).height - before.height) < 0.02);
        await owner.getByRole('button', { name: 'Abrir painel do jogo' }).click();
        await owner.getByText('Oficina do mundo', { exact: true }).click();
        await owner.getByText('Esculpir o terreno', { exact: true }).click();
        const undo = owner.getByRole('button', { name: 'Desfazer edição' });
        assert.equal(await undo.isEnabled(), false);
        await owner.getByRole('button', { name: 'Levantar', exact: true }).click();
        const bounds = await owner.locator('#game-canvas').boundingBox();
        await owner.mouse.click(bounds.x + before.x, bounds.y + before.y);
        await waitFor(() => undo.isEnabled(), 'o servidor não confirmou a edição');
        await waitFor(async () => (await probe(owner)).height > before.height + 0.5 &&
            (await probe(witness)).height > before.height + 0.5, 'a edição não apareceu nos dois clientes');

        await undo.click();
        await waitFor(async () => !await undo.isEnabled(), 'o servidor não confirmou o undo');
        await waitFor(async () => Math.abs((await probe(owner)).height - before.height) < 0.02 &&
            Math.abs((await probe(witness)).height - before.height) < 0.02, 'o replay não restaurou os dois clientes');
        assert.deepEqual(errors, []);
        console.log('OK: pincel e undo renderizados nos dois clientes');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
