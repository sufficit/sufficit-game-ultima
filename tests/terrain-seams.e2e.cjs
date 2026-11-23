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
            if (attempt === 100) throw new Error('O terreno detalhado não carregou');
            await page.waitForTimeout(250);
        }
        const renderer = main ? page : page.workers()[0];
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
            const row = Math.sqrt(vertices);
            let geometryGap = 0;
            for (const kind of [BABYLON.VertexBuffer.PositionKind, BABYLON.VertexBuffer.NormalKind]) {
                const base = center.mesh.getVerticesData(kind);
                for (const [neighbor, alongX] of [[east, true], [north, false]]) {
                    const side = neighbor.mesh.getVerticesData(kind);
                    for (let i = 0; i < row; i++) for (let axis = 0; axis < 3; axis++) {
                        const a = alongX ? i * row + row - 1 : (row - 1) * row + i;
                        const b = alongX ? i * row : i;
                        geometryGap = Math.max(geometryGap, Math.abs(base[3 * a + axis] - side[3 * b + axis]));
                    }
                }
            }
            return { east: compareAxis(east, true), north: compareAxis(north, false), vertices, geometryGap };
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
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
