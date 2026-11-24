// t63 — HUD: o aviso do servidor some sozinho e a atividade da IA aparece no cartão de status recolhido.
// Uso: node tests/hud-status.e2e.js <URL do site>
// Reusa o padrão do tests/ui-mobile.e2e.js (playwright-core + chromium headless shell 1255, render=main).
// A IA na CPU é exercitada sem baixar modelo: a importação do motor wllama fica pendurada
// (estado "downloading") e depois é abortada (estado "error").
'use strict';
const path = require('path');
const { chromium } = require('playwright-core');

const SITE = (process.argv[2] || 'http://127.0.0.1:8093').replace(/\/$/, '');
const EXE = path.join(process.env.HOME, '.cache/ms-playwright/chromium_headless_shell-1255/chrome-headless-shell-linux64/chrome-headless-shell');
const EXPECTED = /ia-cpu|wllama|ERR_FAILED/;   // erros de console previstos ao forçar a falha da IA
const ACTIVITY = /IA \((CPU|GPU)\)/i;          // selos de atividade: "Baixando IA (CPU)", "Erro na IA (GPU)"…

const results = [];
const check = (name, ok, detail) => {
    results.push(ok);
    console.log((ok ? 'ok    ' : 'FALHA ') + name + (detail === undefined ? '' : ' — ' + detail));
};
const STATUS_TEXT = () => (document.querySelector('.ultima-status') || {}).innerText || '';
const oneLine = (text) => text.replace(/\s+/g, ' ').trim().slice(0, 110);

(async () => {
    const browser = await chromium.launch({
        executablePath: EXE, headless: true,
        args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage'],
    });
    try {
        const ctx = await browser.newContext({ viewport: { width: 1100, height: 640 }, deviceScaleFactor: 1 });
        const page = await ctx.newPage();
        const errors = [];
        page.on('pageerror', (e) => errors.push(String(e)));
        page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

        // O rAF engasga no headless com swiftshader: todas as esperas são por polling.
        // innerText aplica o text-transform do CSS: as comparações ignoram maiúsculas.
        const waitStatus = (present, source, ms) => page.waitForFunction(
            (a) => a.present === new RegExp(a.re, 'i').test((document.querySelector('.ultima-status') || {}).innerText || ''),
            { present, re: source }, { timeout: ms || 30000, polling: 250 });
        let pendingImport = null;   // a importação do wllama não responde até ser abortada
        await page.route(/@wllama\/wllama@[^/]+\/esm\/index\.min\.js/, (route) => { pendingImport = route; });

        await page.goto(SITE + '/?render=main&live=0&rede=0', { waitUntil: 'domcontentloaded', timeout: 45000 });
        await page.waitForFunction(() => ((document.querySelector('.ultima-status') || {}).innerText || '').length > 0,
            null, { timeout: 90000, polling: 250 });

        // 1) aviso do servidor: aparece, fica o tempo de leitura e some sozinho
        await page.waitForFunction(() => !!document.querySelector('.ultima-announce'), null, { timeout: 20000, polling: 250 });
        const shownAt = Date.now();
        const announce = (await page.evaluate(() => (document.querySelector('.ultima-announce') || {}).innerText || '')).trim();
        check('aviso do servidor aparece', announce.length > 0, oneLine(announce));
        await page.waitForTimeout(3000);
        check('aviso continua na tela após 3 s', await page.evaluate(() => !!document.querySelector('.ultima-announce')));
        await page.waitForFunction(() => !document.querySelector('.ultima-announce'), null, { timeout: 40000, polling: 250 });
        const goneAfter = (Date.now() - shownAt) / 1000;
        check('aviso some sozinho (tempo de leitura)', goneAfter >= 5 && goneAfter <= 30, goneAfter.toFixed(1) + ' s');

        // 2) atividade da IA no cartão de status recolhido
        let text = await page.evaluate(STATUS_TEXT);
        check('cartão recolhido e sem atividade de IA antes de ativar', !/pos \(/i.test(text) && !ACTIVITY.test(text), oneLine(text));

        await page.evaluate(() => { window.game.aiCpu.load(); });
        await waitStatus(true, 'Baixando IA \\(CPU\\)');
        text = await page.evaluate(STATUS_TEXT);
        check('"Baixando IA (CPU)" aparece com o cartão recolhido', /Baixando IA \(CPU\) · \d+%/i.test(text) && !/pos \(/i.test(text), oneLine(text));

        for (let i = 0; i < 40 && !pendingImport; i++) await page.waitForTimeout(250);
        if (!pendingImport) throw new Error('a importação do wllama não foi solicitada');
        await pendingImport.abort();
        await waitStatus(true, 'Erro na IA \\(CPU\\)');
        text = await page.evaluate(STATUS_TEXT);
        check('"Erro na IA (CPU)" aparece com o cartão recolhido', /Erro na IA \(CPU\)/i.test(text) && !/pos \(/i.test(text), oneLine(text));

        await page.evaluate(() => { window.game.aiCpu.disable(); });
        await waitStatus(false, 'IA \\((CPU|GPU)\\)');
        check('selo some ao desligar a IA', true);

        // 3) os detalhes do cartão continuam funcionando
        await page.evaluate(() => document.querySelector('.ultima-status__more').click());
        await waitStatus(true, 'pos \\(');
        check('botão "detalhes" continua abrindo posição e altitude', true);

        const unexpected = errors.filter((e) => !EXPECTED.test(e));
        check('sem erros de console inesperados', unexpected.length === 0, unexpected.slice(0, 3).join(' | ').slice(0, 300));
    } catch (e) {
        check('exceção durante o teste', false, String((e && e.stack) || e).slice(0, 400));
    } finally {
        await browser.close();
    }
    const failed = results.filter((ok) => !ok).length;
    console.log((results.length - failed) + '/' + results.length + ' verificações: ' + (failed ? 'FALHOU' : 'PASSOU'));
    process.exit(failed ? 1 : 0);
})();
