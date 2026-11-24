// Exercita os serviços C# no Blazor publicado com API isolada via Playwright.
const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const { createHash } = require('node:crypto');

const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const api = new URL('migration-fixture/', site).href.replace(/\/$/, '');
const queueKey = 'ultima-dialog-queue';
const previous = { id: 'd-legacy', t: 1700000000000, npc: 'Guardiã', player: 'Olá', reply: 'Bem-vindo',
    model: '', source: 'servidor · regras', ms: 23 };

async function until(check, label, attempts = 100) {
    for (let attempt = 0; attempt < attempts; attempt++) {
        if (await check()) return;
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error(label);
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean) });
    try {
        const context = await browser.newContext({ viewport: { width: 1200, height: 720 } });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(String(error)));
        const glb = await (await context.request.get(new URL('assets/prop.stump.glb', site).href)).body();
        assert.equal(glb.subarray(0, 4).toString(), 'glTF');
        const sha256 = createHash('sha256').update(glb).digest('hex');
        const asset = { id: 'migration.good', file: 'good.glb', sha256, scaleM: 0.8 };
        const invalid = { id: 'migration.bad', file: 'bad.glb', sha256: '0'.repeat(64) };
        const generated = { ...asset, id: 'migration.generated' };
        const uploaded = { ...asset, id: 'migration.upload' };
        const batches = [];
        const uploads = [];
        const worldCommands = [];
        let rejectBatches = true;
        let generationToken;

        await page.addInitScript(({ key, turn }) => {
            localStorage.setItem(key, JSON.stringify([turn]));
            Object.defineProperty(navigator, 'clipboard', {
                value: { writeText: async text => { window.migrationCopied = text; } }
            });
        }, { key: queueKey, turn: previous });

        await page.route(api + '/**', async route => {
            const request = route.request();
            const url = new URL(request.url());
            const path = url.pathname.split('/migration-fixture')[1];
            if (path === '/v1/game/sessions') return route.fulfill({ json: {
                token: 'migration-session', character: { id: 'migration-test-player' }
            } });
            if (path === '/v1/game/events') {
                await new Promise(resolve => setTimeout(resolve, 500));
                return route.fulfill({ json: { events: url.searchParams.get('since') === '0' ? [
                    { type: 'snapshot', seq: 1, characters: [{ id: 'other', name: 'Visitante', x: 0, z: 8, yaw: 0 }] },
                    { type: 'say', seq: 2, from: 'other', name: 'Visitante', text: 'Olá do servidor fictício' }
                ] : [] } });
            }
            if (path === '/v1/game/map') return route.fulfill({ json: { revision: 1, placements: [
                { kind: 'place', uid: 'migration-map', asset: 'prop.stump', x: 4, z: 7, scale: 1 }
            ] } });
            if (path === '/v1/game/command') {
                worldCommands.push({ ...request.postDataJSON(), token: request.headers()['x-game-token'] });
                return route.fulfill({ json: { ok: true } });
            }
            if (path === '/v1/assets') return route.fulfill({ json: { assets: [asset, invalid] } });
            if (path === '/v1/assets/generate') {
                generationToken = request.headers()['x-game-token'];
                assert.deepEqual(request.postDataJSON(), { prompt: 'Cristal de teste' });
                return route.fulfill({ json: { asset: generated } });
            }
            if (path === '/v1/assets/upload') {
                uploads.push({ name: url.searchParams.get('name'), token: request.headers()['x-game-token'],
                    mime: request.headers()['content-type'], bytes: request.postDataBuffer()?.length });
                return route.fulfill({ json: { asset: uploaded } });
            }
            if (path.endsWith('.glb')) return route.fulfill({ body: glb, contentType: 'model/gltf-binary' });
            if (path === '/v1/analytics/dialogs') {
                const submitted = request.postDataJSON();
                batches.push(submitted);
                const ids = submitted.turns.filter(turn => turn.id.length <= 40).map(turn => turn.id);
                return rejectBatches ? route.fulfill({ status: 503 }) : route.fulfill({ json: { ok: true, ids } });
            }
            return route.fulfill({ json: {} });
        });

        await page.route('**/build.json', route => route.fulfill({ json: { build: 'fixture-release' } }));
        await page.route(url => url.pathname === new URL(site).pathname && url.searchParams.has('migrationTest'),
            async route => {
                const original = await route.fetch();
                const html = (await original.text()).replace(/(<meta name="ultima-build" content=")[^"]+/, '$1migration-test');
                await route.fulfill({ response: original, body: html });
            });

        const url = new URL(site);
        url.search = new URLSearchParams({ api, rede: '0', sala: 'jardim', migrationTest: '1' }).toString();
        await page.goto(url.href, { waitUntil: 'domcontentloaded' });
        await until(() => page.evaluate(async () => (await game?.dialogLog?.debug())?.queued === 1), 'fila anterior restaurada');

        await page.getByRole('alert').getByText('Nova versão disponível (fixture).').waitFor();
        await page.getByRole('button', { name: 'Depois', exact: true }).click();
        await page.getByRole('alert').getByText('Nova versão disponível (fixture).').waitFor({ state: 'hidden' });

        await page.evaluate(async () => {
            await game.dialogLog.turn('Guard<i>ã', 'oi\t <caminhante>', 'Sim', 'servidor · regras', 100);
            await game.dialogLog.turn('Guard<i>ã', 'oi\t <caminhante>', 'Sim', 'servidor · regras', 100);
        });
        let queued = await page.evaluate(key => JSON.parse(localStorage.getItem(key)), queueKey);
        assert.equal(queued.length, 2);
        assert.equal(queued[0].id, previous.id);
        assert.equal(queued[1].npc, 'Guardiã');
        assert.equal(queued[1].player, 'oi caminhante');
        assert.equal(await page.evaluate(() => game.dialogLog.flush()), false);
        assert.equal((await page.evaluate(() => game.dialogLog.debug())).queued, 2);
        await until(() => page.evaluate(async id => (await game.glb.status()).loaded.includes(id + ' (runtime)'), asset.id),
            'catálogo e GLB verificado');
        assert.ok(!(await page.evaluate(id => game.glb.get(id), invalid.id)));

        await until(() => page.evaluate(() => game.live.token() === 'migration-session'), 'sessão da API iniciada');
        await page.getByText('Olá do servidor fictício').waitFor();
        await until(() => {
            const renderer = page.workers()[0] || page;
            return renderer.evaluate(() => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                return scene.getMeshByName('place-migration-map') && !!scene.getTransformNodeByName('remote-other');
            });
        }, 'pose remota e mapa aplicados ao motor');
        await page.evaluate(() => game.net.sendChat('Olá da aplicação'));
        await until(() => worldCommands.some(command => command.action === 'say' && command.token === 'migration-session'),
            'comando de chat autenticado');
        assert.ok(worldCommands.some(command => command.action === 'pose' && typeof command.yaw === 'number'));
        // Terreno WASM constrói os chunks numa fila orçada; o clique de escultura só
        // acerta o raycast quando o chão sob o ponto já existe (bancada: 25 chunks
        // aos ~52 s, fila zerando aos ~119 s). O teste de réplica usa exatamente a
        // mesma chamada scene.pick do motor, então a porta cobre o caso real.
        await until(() => {
            const renderer = page.workers()[0] || page;
            return renderer.evaluate(() => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                if (!scene) { return false; }
                const pick = scene.pick(650, 380, m => !!(m && m.name && m.name.indexOf('ground-') === 0));
                return !!(pick && pick.hit);
            });
        }, 'terreno visível sob o ponto de escultura', 600);
        await page.evaluate(async () => {
            await game.build.mode('raise', { r: 1.5 });
            game.terrainClick(650, 380);
        });
        await until(() => worldCommands.some(command => command.action === 'sculpt' && command.r === 1.5
            && command.token === 'migration-session'), 'edição autenticada enviada pela Oficina');
        await page.evaluate(() => game.build.mode('off'));
        await page.getByRole('button', { name: 'Abrir painel do jogo' }).click();
        await page.getByText('Oficina do mundo', { exact: true }).click();
        await page.getByText('Criar modelo', { exact: true }).click();
        await page.locator('#workshop-prompt').fill('Cristal de teste');
        await page.getByRole('button', { name: 'Gerar', exact: true }).click();
        await page.getByText('✓ "Cristal de teste" criado e plantado no mundo!').waitFor();
        assert.equal(generationToken, 'migration-session');

        await page.getByText('Enviar modelo', { exact: true }).click();
        await page.locator('#workshop-file').setInputFiles({ name: 'toco.glb', mimeType: 'model/gltf-binary', buffer: glb });
        await page.getByText('✓ "toco" publicado no mundo!').waitFor();
        assert.deepEqual(uploads, [{ name: 'toco', token: 'migration-session', mime: 'model/gltf-binary', bytes: glb.length }]);

        await page.getByText('Multiplayer e voz', { exact: true }).click();
        await page.getByRole('button', { name: 'Copiar convite' }).click();
        const copied = new URL(await page.evaluate(() => window.migrationCopied));
        assert.equal(copied.searchParams.get('sala'), 'jardim');
        assert.equal(copied.searchParams.get('api'), api);
        rejectBatches = false;
        await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
        await until(() => page.evaluate(async () => (await game.dialogLog.debug()).queued === 0), 'envio e confirmação por pagehide');
        assert.equal(batches.at(-1).turns.length, 2);
        assert.deepEqual(errors, []);
        console.log('Migração validada: atualização, diálogo legado/reenvio, sessão, mapa, comandos, SHA-256, geração, upload e convite.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
