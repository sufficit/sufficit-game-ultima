// t50 — validação local do registro de conversas (dialog-log) com Playwright via node.
// Uso: node tests/dialog-log.e2e.js <URL do site> <URL da API>
// Fluxo: abre o jogo, anda até a Guardiã, E → pergunta → resposta por regras,
// confere a fila em localStorage, flush() → POST 200, GET traz o lote,
// pagehide envia e limpa a fila.
'use strict';
const path = require('path');
const { chromium } = require('playwright-core'); // npm i playwright-core; navegadores em ~/.cache/ms-playwright

const SITE = process.argv[2] || 'http://127.0.0.1:8091';
const API = process.argv[3] || 'http://127.0.0.1:8090';
const EXE = path.join(process.env.HOME, '.cache/ms-playwright/chromium_headless_shell-1255/chrome-headless-shell-linux64/chrome-headless-shell');

function fail(msg) { console.error('FALHOU: ' + msg); process.exitCode = 1; }
const ok = (label, cond, extra) => console.log((cond ? 'ok  ' : 'FALHOU ') + label + (extra ? ' — ' + extra : '')) || (!cond && fail(label));

(async () => {
    const browser = await chromium.launch({
        executablePath: EXE,
        headless: true,
        args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage'],
    });
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const page = await ctx.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(String(e)));
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });

    // Mesma receita do fluxo validado localmente via MCP: worker (sem render=main),
    // teleporte pela URL + chamada ao atalho do core, dev=1.
    await page.goto(SITE + '/?api=' + encodeURIComponent(API) + '&teleportToGuardian=1&dev=1', { waitUntil: 'domcontentloaded', timeout: 45000 });
    // Motor + Blazor prontos: canvas existe e game.dialogLog disponível.
    await page.waitForFunction(() => window.game && window.game.dialogLog && document.getElementById('game-canvas'), null, { timeout: 45000 });
    ok('módulo dialogLog carregado', true);

    // IA da CPU servida e painel presente (frente 84d2e9e, validação pendente).
    const aiCpu = await page.evaluate(async () => (await fetch('js/ai-cpu.js')).status);
    ok('js/ai-cpu.js servido (200)', aiCpu === 200, 'HTTP ' + aiCpu);

    // Chega perto da Guardiã (NPC em z=-14) e abre o diálogo com E.
    // Atalho de dev primeiro (determinístico); caminhar com W é o fallback.
    await page.waitForSelector('#game-canvas', { timeout: 10000 });
    const teleported = await page.evaluate(() => { try { window.game.teleportToGuardian(); return true; } catch (e) { return false; } });
    if (!teleported) await page.keyboard.down('KeyW');
    await page.waitForFunction(() => {
        const t = document.querySelector('.ultima-status') || document.body;
        return document.querySelector('.ultima-hint, .ultima-dialog') !== null;
    }, null, { timeout: 20000 }).catch(() => {});
    await page.keyboard.up('KeyW');
    await page.keyboard.press('KeyE');
    const dialog = await page.waitForSelector('.ultima-dialog', { timeout: 15000 }).catch(() => null);
    ok('diálogo aberto com a Guardiã (E)', !!dialog);
    if (!dialog) { await page.screenshot({ path: '/tmp/ultima-t50/e2e-no-dialog.png', timeout: 5000 }).catch(() => {}); }

    if (dialog) {
        await page.waitForSelector('#dialog-input', { timeout: 10000 });
        await page.fill('#dialog-input', 'Onde fica o santuário?');
        await page.press('#dialog-input', 'Enter');
        // Resposta por regras substitui as reticências.
        const reply = await page.waitForFunction(() => {
            const el = document.querySelector('.ultima-dialog__line');
            return el && el.textContent && !el.textContent.includes('…') ? el.textContent : false;
        }, null, { timeout: 30000 }).catch(() => null);
        ok('resposta da Guardiã chegou', !!reply, reply ? String(reply).slice(0, 60) : 'sem resposta');
    }

    // Fila persistente com o turno capturado no funil único.
    await page.waitForTimeout(600);
    const queue0 = await page.evaluate(() => JSON.parse(localStorage.getItem('ultima-dialog-queue') || '[]'));
    ok('turno na fila (ultima-dialog-queue)', queue0.length >= 1, JSON.stringify(queue0.slice(-1)[0] || null).slice(0, 160));
    const dbg = await page.evaluate(() => window.game.dialogLog.debug());
    ok('debug() expõe fila/api', dbg && typeof dbg.queued === 'number' && dbg.api.includes('/v1/analytics/dialogs'), JSON.stringify(dbg).slice(0, 140));

    // flush() manual: POST 200 e confirmação remove os ids.
    const flushed = await page.evaluate(() => window.game.dialogLog.flush());
    await page.waitForTimeout(800);
    const queue1 = await page.evaluate(() => JSON.parse(localStorage.getItem('ultima-dialog-queue') || '[]'));
    ok('flush() enviou e esvaziou a fila', flushed === true && queue1.length === 0, 'fila agora ' + queue1.length);

    // GET traz o lote no backend.
    const got = await page.evaluate(async (api) => {
        const r = await fetch(api + '/v1/analytics/dialogs?limit=10');
        return await r.json();
    }, API);
    ok('GET traz o lote', got && got.total >= 1 && got.turns.length >= 1, 'total=' + (got && got.total));

    // pagehide envia e limpa (novo turno simulado pelo próprio funil público).
    await page.evaluate(() => window.game.dialogLog.turn('Guardiã', 'teste pagehide', 'resposta pagehide', 'servidor · regras', 42));
    await page.waitForTimeout(200);
    await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
    await page.waitForTimeout(900);
    const queue2 = await page.evaluate(() => JSON.parse(localStorage.getItem('ultima-dialog-queue') || '[]'));
    ok('pagehide enviou e limpou a fila', queue2.length === 0, 'fila agora ' + queue2.length);

    const relevant = errors.filter(e => !/favicon|wllama|Download the React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
    ok('console sem erros relevantes', relevant.length === 0, relevant.slice(0, 3).join(' | ').slice(0, 200));
    await browser.close();
})().catch(e => { fail(e && e.stack || e); process.exit(1); });
