// Valida as texturas e a malha em dois lados de uma divisa de chunks.
const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');

const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const main = process.env.ULTIMA_MODE === 'main';
const procedural = process.env.ULTIMA_PROCEDURAL === '1';

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean) });
    try {
        const page = await browser.newPage({ viewport: { width: 1200, height: 720 } });
        if (procedural) await page.route('**/terrain/*.webp*', route => route.abort());
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = new URL(site);
        url.search = new URLSearchParams({ live: '0', rede: '0', api: 'http://127.0.0.1:19999',
            ...(main ? { render: 'main', gpu: '0' } : {}) }).toString();
        await page.goto(url.href);
        if (process.env.ULTIMA_BUILD)
            assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
        for (let attempt = 0; ; attempt++) {
            if (await page.evaluate(async materials => {
                if (!window.game?.weather?.status || !window.game?.models?.status) return false;
                return (await game.weather.status())?.materials === materials &&
                    (await game.models.status())?.queue === 0 && (await game.build.state())?.chunks >= 25;
            }, procedural ? 0 : 2)) break;
            // ULTIMA_WAIT_TRIES amplia a janela (padrão 100 = 25 s); bancadas com renderização por
            // software e carga alta levam ~2 min para o terreno WASM e a fila de modelos esvaziarem.
            if (attempt === (Number(process.env.ULTIMA_WAIT_TRIES) || 100)) throw new Error('O terreno detalhado não carregou');
            await page.waitForTimeout(250);
        }
        const renderer = main ? page : page.workers()[0];
        assert.equal(await page.evaluate(() => game.mode), main ? 'main' : 'worker');
        assert.equal((await page.evaluate(() => game.debugChunk(0, 0))).terrainEngine, 'csharp-wasm');
        const compare = () => renderer.evaluate(() => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const canvasPixels = name => {
                const mesh = scene.getMeshByName('ground-' + name);
                const tex = mesh.material.diffuseTexture;
                const { width, height } = tex.getSize();
                return { mesh, tex, width, height, colors: tex.getContext().getImageData(0, 0, width, height).data };
            };
            const center = canvasPixels('0,0');
            const east = canvasPixels('1,0');
            const north = canvasPixels('0,1');
            function sample({ tex, width, height, colors }, u, v, channel) {
                const x = Math.min(width - 1, Math.max(0, (u * tex.uScale + tex.uOffset) * width - 0.5));
                const y = Math.min(height - 1, Math.max(0, (v * tex.vScale + tex.vOffset) * height - 0.5));
                const left = Math.floor(x), top = Math.floor(y);
                const right = Math.min(width - 1, left + 1), bottom = Math.min(height - 1, top + 1);
                const pixel = (x, y) => colors[(y * width + x) * 4 + channel];
                const a = pixel(left, top) + (x - left) * (pixel(right, top) - pixel(left, top));
                const b = pixel(left, bottom) + (x - left) * (pixel(right, bottom) - pixel(left, bottom));
                return a + (y - top) * (b - a);
            }
            function compareAxis(neighbor, alongX) {
                let sum = 0;
                for (let i = 0; i < 128; i++) for (let channel = 0; channel < 3; channel++) {
                    const t = (i + 0.5) / 128;
                    const a = sample(center, alongX ? 1 : t, alongX ? t : 1, channel);
                    const b = sample(neighbor, alongX ? 0 : t, alongX ? t : 0, channel);
                    sum += Math.abs(a - b);
                }
                return sum / (128 * 3);
            }
            const vertices = center.mesh.getTotalVertices();
            let geometryGap = 0;
            function edge(mesh, alongX) {
                const positions = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
                const normals = mesh.getVerticesData(BABYLON.VertexBuffer.NormalKind);
                const points = new Map();
                for (let i = 0; i < positions.length; i += 3) {
                    if (positions[i + (alongX ? 0 : 2)] !== 16) continue;
                    points.set(positions[i + (alongX ? 2 : 0)], [positions[i + 1], ...normals.slice(i, i + 3)]);
                }
                return points;
            }
            for (const [neighbor, alongX] of [[east, true], [north, false]]) {
                const base = edge(center.mesh, alongX);
                const side = edge(neighbor.mesh, alongX);
                if (base.size !== side.size) throw new Error('as bordas têm topologias diferentes');
                for (const [key, a] of base) {
                    const b = side.get(key);
                    if (!b) throw new Error('um vértice da emenda não tem par');
                    for (let axis = 0; axis < a.length; axis++)
                        geometryGap = Math.max(geometryGap, Math.abs(a[axis] - b[axis]));
                }
            }
            let wallError = 0;
            const down = new BABYLON.Vector3(0, -1, 0);
            for (const radius of [0.54, 0.68, 0.82, 0.94]) for (let i = 0; i < 48; i++) {
                const angle = i * Math.PI / 24;
                const x = 15.82 + Math.cos(angle) * radius;
                const z = 16.05 + Math.sin(angle) * radius;
                const ray = new BABYLON.Ray(new BABYLON.Vector3(x, 100, z), down);
                const visible = scene.pickWithRay(ray, mesh => mesh.name.startsWith('ground-')).pickedPoint.y;
                wallError = Math.max(wallError, Math.abs(visible - game.heightAt(x, z)));
            }
            return { east: compareAxis(east, true), north: compareAxis(north, false), vertices, geometryGap, wallError };
        });
        const initial = await compare();
        await page.evaluate(() => {
            game.setCollectedResources(Array.from({ length: 6 }, (_, i) => ({
                id: 'seam-dig-' + i, kind: 'soil', x: 15.82, z: 16.05
            })));
            game.applyWorldOp({ kind: 'paint', mat: 'dirt', x: 16, z: 14, r: 4 });
        });
        await page.waitForTimeout(350);
        const edited = await compare();
        console.log({ mode: main ? 'main' : 'worker', procedural, initial, edited });
        for (const sample of [initial, edited]) {
            assert.ok(sample.east < 1 && sample.north < 1, 'o mesmo ponto deve ter a mesma cor nos dois chunks');
            assert.ok(sample.vertices >= 4225, 'a malha deve resolver curvas em escala inferior a 0,67 m');
            assert.ok(sample.geometryGap < 0.00001, 'alturas e normais precisam coincidir na borda');
        }
        assert.equal(initial.vertices, 4225, 'mantém a malha estável fora das edições');
        assert.ok(edited.wallError < 0.16, 'a malha deve seguir a curvatura da parede escavada');
        const otherChunkVertices = () => renderer.evaluate(() =>
            BABYLON.EngineStore.LastCreatedScene.getMeshByName('ground--1,0').getTotalVertices());
        await page.evaluate(() => game.setWorldEdits([{ kind: 'sculpt', x: -24, z: 4, r: 2, dh: -2 }], 1));
        await page.waitForTimeout(250);
        assert.ok(await otherChunkVertices() > 4225, 'refina a escultura compartilhada');
        await page.evaluate(() => game.setWorldEdits([], 2));
        await page.waitForTimeout(250);
        assert.equal(await otherChunkVertices(), 4225, 'volta à malha base ao remover a escultura');
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
