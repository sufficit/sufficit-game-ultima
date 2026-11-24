const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');

const main = process.env.ULTIMA_MODE === 'main';
const site = new URL(process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/');
site.search = new URLSearchParams({
    live: '0', rede: '0', gpu: '0', api: 'http://127.0.0.1:19999', ...(main ? { render: 'main' } : {})
});

async function waitFor(check, description) {
    // ULTIMA_WAIT_TRIES amplia a janela (padrão 120 = 30 s) para bancadas lentas.
    const tries = Number(process.env.ULTIMA_WAIT_TRIES) || 120;
    for (let attempt = 0; attempt < tries; attempt++) {
        const result = await check();
        if (result) return result;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(description);
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean) });
    try {
        const page = await browser.newPage({ viewport: { width: 800, height: 450 } });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        const chunk = () => page.evaluate(() => game.debugChunk(0, 0));
        const ready = async () => {
            if (process.env.ULTIMA_BUILD)
                assert.equal(await page.locator('meta[name="ultima-build"]').getAttribute('content'), process.env.ULTIMA_BUILD);
            await waitFor(() => page.evaluate(async () => (await window.game?.build?.state?.())?.chunks >= 25),
                'os chunks não carregaram');
            assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
        };
        const currentRenderer = () => main ? page : page.workers()[0];
        const fileExists = () => currentRenderer().evaluate(async () => {
            const root = await navigator.storage.getDirectory();
            const storage = await root.getDirectoryHandle('ultima-assets');
            const key = await game.assets.digest(['terrain-mesh-v1', 0, 0]);
            try { return !!(await storage.getFileHandle(key)); }
            catch (e) { if (e.name === 'NotFoundError') return false; throw e; }
        });
        await page.goto(site.href);
        await ready();
        assert.equal((await chunk()).meshSource, 'generated', 'a visita fria gera a malha');
        await waitFor(fileExists, 'a visita fria precisa persistir a malha por build e chunk');
        await waitFor(() => page.evaluate(async () => {
            const models = await game.models.status();
            return models.queue === 0 && models.loaded.length >= 6;
        }), 'a fila de modelos não drenou');
        assert.equal(await page.evaluate(async () => (await game.perf.report()).budgetMs), 4);
        await page.reload();
        await ready();
        assert.equal((await chunk()).meshSource, 'opfs', 'a revisita precisa usar a malha persistida');
        const renderer = currentRenderer();
        const signature = () => currentRenderer().evaluate(() => {
            const mesh = BABYLON.EngineStore.LastCreatedScene.getMeshByName('ground-0,0');
            const positions = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
            return [positions.length, positions[3 * (65 * 32 + 32) + 1]];
        });
        const base = await signature();
        await page.evaluate(() => game.applyWorldOp({ kind: 'sculpt', x: 0, z: 0, r: 2, dh: -2 }));
        assert.equal((await chunk()).meshSource, 'generated');
        assert.ok((await signature())[1] < base[1] - 1.9, 'a edição não pode usar a malha base');
        await page.evaluate(() => game.setWorldEdits([], 2));
        assert.deepEqual(await signature(), base);
        await renderer.evaluate(async () => {
            const root = await navigator.storage.getDirectory();
            const storage = await root.getDirectoryHandle('ultima-assets');
            const key = await game.assets.digest(['terrain-mesh-v1', 0, 0]);
            const handle = await storage.getFileHandle(key);
            const bytes = new Uint8Array(await (await handle.getFile()).arrayBuffer());
            bytes[bytes.length - 1] ^= 1;
            const writable = await handle.createWritable();
            await writable.write(bytes);
            await writable.close();
        });
        await page.reload();
        await ready();
        assert.equal((await chunk()).meshSource, 'generated', 'uma falha de integridade deve regenerar a malha');
        assert.deepEqual(await signature(), base);
        assert.deepEqual(errors, []);
        console.log({ mode: main ? 'main' : 'worker', warm: 'opfs', edited: 'generated', corrupted: 'recovered' });
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
