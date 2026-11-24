// Exercita o relógio, os astros e o clima com instantes conhecidos.
const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');

const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const main = process.env.ULTIMA_MODE === 'main';
const viewport = process.env.ULTIMA_MOBILE === '1' ? { width: 390, height: 844 } : { width: 1200, height: 720 };
const day = new Date('2026-06-21T12:00:00Z');
const night = new Date('2026-11-24T00:30:00Z');

async function waitForWorld(page, predicate, message) {
    for (let attempt = 0; attempt < 240; attempt++) {
        if (await page.evaluate(predicate)) return;
        await page.waitForTimeout(250);
    }
    throw Error(message);
}

async function open(browser, date) {
    const page = await browser.newPage({ viewport, timezoneId: 'America/Sao_Paulo' });
    const errors = [];
    const diagnostics = [];
    page.on('console', message => { if (message.type() === 'warning') diagnostics.push(message.text()); });
    page.on('pageerror', error => errors.push(error.message));
    await page.clock.setFixedTime(date);
    if (!main) {
        await page.route('**/js/game-core.js*', async route => {
            const response = await route.fetch();
            // A emulação do relógio da página não chega ao worker de renderização.
            const body = (await response.text()).replace('terrain.SampleSky(Date.now())',
                `terrain.SampleSky(${date.getTime()})`);
            await route.fulfill({ response, body });
        });
    }
    const url = new URL(site);
    url.search = new URLSearchParams({ live: '0', rede: '0', gpu: '0', api: 'http://127.0.0.1:19999',
        ...(main ? { render: 'main' } : {}) });
    await page.goto(url.href);
    await waitForWorld(page, async () => !!(await window.game?.skyStatus?.()), 'céu carregado');
    if (process.env.ULTIMA_BUILD)
        assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
    await page.waitForFunction(expected => game.mode === expected, main ? 'main' : 'worker', { timeout: 30000 })
        .catch(error => { throw Error([...errors, ...diagnostics, error.message].join('\n')); });
    return { page, errors, renderer: main ? page : page.workers()[0] };
}

async function openPanel(page) {
    const menu = viewport.width < 600 ? 'Abrir menu' : 'Abrir painel do jogo';
    await page.getByRole('button', { name: menu, exact: true }).click();
    await page.getByRole('button', { name: 'Tempo e clima' }).click();
    await page.waitForFunction(() => document.getElementById('world-time-local')?.textContent.includes('GMT'));
}

const renderState = renderer => renderer.evaluate(() => {
    const scene = BABYLON.EngineStore.LastCreatedScene;
    return {
        direct: scene.getLightByName('sun').intensity,
        ambient: scene.getLightByName('hemi').intensity,
        directionY: scene.getLightByName('sun').direction.y,
        moon: scene.getMeshByName('moon').visibility,
        fog: scene.fogDensity
    };
});

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean) });
    try {
        const { page, renderer, errors } = await open(browser, day);
        const solar = await page.evaluate(() => game.skyStatus());
        const clear = await renderState(renderer);
        assert.ok(solar[1] > .87 && solar[7] === 1 && clear.direct > .9 && clear.directionY < -.87,
            'Sol alto de verão ilumina a cena');
        await openPanel(page);
        if (process.env.ULTIMA_SCREENSHOTS) {
            await waitForWorld(page, async () => (await game.build.state())?.chunks >= 25, 'terreno do dia carregado');
            await page.screenshot({ path: process.env.ULTIMA_SCREENSHOTS + '-menu.png' });
        }
        assert.match(await page.locator('#world-time-gmt').innerText(), /GMT 21\/06\/2026 12:00/);
        assert.match(await page.locator('#world-time-local').innerText(), /09:00 GMT-3/);
        assert.match(await page.locator('#world-time-zone').getAttribute('placeholder'), /Automático.*Sao Paulo/);
        await page.locator('#world-time-zone').fill('London');
        await page.getByRole('option', { name: 'Europe/London', exact: true }).click();
        await page.waitForFunction(() => document.getElementById('world-time-local').textContent.includes('13:00'));
        assert.match(await page.locator('#world-time-local').innerText(), /13:00 GMT\+1/);
        assert.equal(await page.evaluate(() => localStorage.getItem('ultima.fuso')), 'Europe/London');
        assert.deepEqual(await page.evaluate(() => game.skyStatus()), solar, 'o fuso não altera o céu');
        await page.locator('#weather-mode').click();
        await page.getByRole('option', { name: 'Chuva forte', exact: true }).click();
        await waitForWorld(page, async () => {
            const weather = await game.weather.status();
            return weather.mode === 'heavy-rain' && weather.rain > 1.2 && weather.drops > 0;
        }, 'chuva forte aplicada na cena');
        const rainy = await renderState(renderer);
        assert.ok(rainy.direct < clear.direct * .6, JSON.stringify({ clear, rainy }));
        await page.locator('#weather-mode').click();
        await page.getByRole('option', { name: 'Neblina', exact: true }).click();
        await waitForWorld(page, async () => {
            const weather = await game.weather.status();
            return weather.mode === 'fog' && weather.rain === 0;
        }, 'neblina sem chuva');
        assert.ok((await renderState(renderer)).fog > clear.fog * 4);
        await page.reload();
        await waitForWorld(page, async () => !!(await window.game?.skyStatus?.()), 'céu carregado');
        await openPanel(page);
        assert.match(await page.locator('#world-time-local').innerText(), /13:00 GMT\+1/);
        assert.match(await page.locator('#weather-mode').innerText(), /Neblina/);
        await page.getByRole('button', { name: 'Usar fuso automático' }).click();
        await page.waitForFunction(() => document.getElementById('world-time-local').textContent.includes('09:00'));
        assert.deepEqual(errors, []);
        await page.close();

        const dark = await open(browser, night);
        const lunar = await dark.page.evaluate(() => game.skyStatus());
        const nightLight = await renderState(dark.renderer);
        assert.ok(lunar[1] < 0 && lunar[4] > 0 && lunar[6] > .98 && lunar[9] === 4);
        assert.ok(nightLight.direct === 0 && nightLight.ambient < clear.ambient && nightLight.moon === 1);
        if (process.env.ULTIMA_SCREENSHOTS) {
            await waitForWorld(dark.page, async () => (await game.build.state())?.chunks >= 25, 'terreno da noite carregado');
            await dark.page.screenshot({ path: process.env.ULTIMA_SCREENSHOTS + '-noite.png' });
        }
        await openPanel(dark.page);
        assert.match(await dark.page.locator('#world-time-gmt').innerText(), /24\/11\/2026 00:30/);
        assert.match(await dark.page.locator('#world-time-local').innerText(), /23\/11\/2026.*21:30 GMT-3/);
        assert.match(await dark.page.locator('#world-moon-phase').innerText(), /Lua cheia/);
        assert.deepEqual(dark.errors, []);
        console.log(JSON.stringify({ mode: main ? 'main' : 'worker', clear, nightLight, moon: lunar[6], viewport }));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
