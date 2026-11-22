// t56 — UI mobile do Ultima: medição e validação com Playwright via node.
// Uso: node tests/ui-mobile.e2e.js <URL do site> [--baseline]
//   --baseline  apenas mede a geometria atual (linha de base "antes"), sempre exit 0.
// Sem --baseline, valida os critérios de aceite mobile (toque, menu, diálogo,
// rotação, desktop sem regressão) e exit != 0 em qualquer falha.
// Reusa o padrão do tests/dialog-log.e2e.js (playwright-core + chromium headless
// shell 1255, render=main para o motor na thread principal).
'use strict';
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright-core');

const SITE = process.argv[2] || 'http://127.0.0.1:8092';
const BASELINE = process.argv.includes('--baseline');
const EXE = path.join(process.env.HOME, '.cache/ms-playwright/chromium_headless_shell-1255/chrome-headless-shell-linux64/chrome-headless-shell');
const SHOTS = '/tmp/mobile-shots';
fs.mkdirSync(SHOTS, { recursive: true });

const VIEWPORTS = {
    portrait: { width: 390, height: 844, mobile: true },
    landscape: { width: 844, height: 390, mobile: true },
    desktop: { width: 1280, height: 720, mobile: false },
    tablet: { width: 768, height: 1024, mobile: true }, // smoke (fica >760px → desktop)
};
const HUD = [
    '.ultima-status', '.workshop-panel', '.ultima-panel-toggle', '.ultima-panel',
    '.ultima-chat-log', 'form.ultima-chat-compose', '.ultima-dialog', '.ultima-menu',
    '#ultima-menu-fab', '.ultima-menu-backdrop',
];
const TOUCH = ['#move-stick', '#move-thumb', '#look-stick', '#look-thumb', '#touch-action', '#touch-cancel'];

function fail(msg) { console.error('FALHOU: ' + msg); process.exitCode = 1; }
const ok = (label, cond, extra) => { console.log((cond ? 'ok  ' : 'FALHOU ') + label + (extra ? ' — ' + extra : '')); if (!cond) fail(label); };

// ---- medições dentro da página -------------------------------------------------
const MEASURE_JS = `
(sel, touchSel) => {
    const visible = (el) => {
        if (!el) return false;
        const r = el.getBoundingClientRect();
        if (r.width < 1 || r.height < 1) return false;
        const s = getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    };
    const rect = (sel) => {
        const el = document.querySelector(sel);
        if (!el || !visible(el)) return null;
        const r = el.getBoundingClientRect();
        return { x: +r.x.toFixed(1), y: +r.y.toFixed(1), w: +r.width.toFixed(1), h: +r.height.toFixed(1) };
    };
    const out = { viewport: { w: innerWidth, h: innerHeight }, rects: {}, touch: {}, page: {} };
    for (const s of sel) { const rr = rect(s); if (rr) out.rects[s] = rr; }
    for (const s of touchSel) { const rr = rect(s); if (rr) out.touch[s] = rr; }
    const canvas = document.getElementById('game-canvas');
    const cr = canvas && canvas.getBoundingClientRect();
    out.canvas = cr ? { x: +cr.x.toFixed(1), y: +cr.y.toFixed(1), w: +cr.width.toFixed(1), h: +cr.height.toFixed(1) } : null;
    out.page.scrollW = document.scrollingElement.scrollWidth;
    out.page.clientW = document.scrollingElement.clientWidth;
    out.page.scrollH = document.scrollingElement.scrollHeight;
    out.page.clientH = document.scrollingElement.clientHeight;
    // malhas da cena (modo main-thread: scene no window.game)
    let meshes = 0;
    try { meshes = (window.game && window.game.scene && window.game.scene.meshes || []).length; } catch (e) {}
    out.meshes = meshes;
    // alvos tocáveis visíveis < 48px
    const small = [];
    try {
        const els = document.querySelectorAll('button, input, select, textarea, a[href], summary, [role="button"]');
        for (const el of els) {
            if (!visible(el) || el.closest('.ultima-chat-log')) continue;
            const r = el.getBoundingClientRect();
            if (r.width < 48 || r.height < 48) {
                small.push((el.id ? '#' + el.id : el.tagName.toLowerCase()) + ' ' + r.width.toFixed(0) + 'x' + r.height.toFixed(0));
            }
        }
    } catch (e) {}
    out.smallTouchTargets = small.slice(0, 12);
    // sobreposições entre elementos de HUD visíveis
    const overlaps = [];
    const names = [...sel, ...touchSel];
    const rects = names.map((n) => ({ n, r: rect(n) })).filter((o) => o.r);
    for (let i = 0; i < rects.length; i++) for (let j = i + 1; j < rects.length; j++) {
        const a = rects[i].r, b = rects[j].r;
        const ix = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
        const iy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
        if (ix > 2 && iy > 2) overlaps.push(rects[i].n + ' ∩ ' + rects[j].n + ' ' + ix.toFixed(0) + 'x' + iy.toFixed(0));
    }
    out.overlaps = overlaps;
    return out;
}`;

const PLAYER_JS = `
(() => {
    try {
        const p = window.game && window.game.player;
        if (p && p.position) return { x: +p.position.x.toFixed(2), z: +p.position.z.toFixed(2), src: 'game.player' };
    } catch (e) {}
    const t = (document.querySelector('.ultima-status') || {}).textContent || '';
    const m = t.match(/(-?\\d+[.,]\\d+)\\s*[,/]\\s*(-?\\d+[.,]\\d+)/);
    return m ? { x: parseFloat(m[1].replace(',', '.')), z: parseFloat(m[2].replace(',', '.')), src: 'status' } : null;
})()`;
const YAW_JS = `
(() => {
    try {
        const c = window.game && (window.game.camera || (window.game.scene && window.game.scene.activeCamera));
        if (c && c.rotation) return +c.rotation.y.toFixed(3);
        if (window.game && typeof window.game.yaw === 'number') return +window.game.yaw.toFixed(3);
    } catch (e) {}
    return null;
})()`;

async function measure(page, tag) {
    return await page.evaluate(MEASURE_JS, HUD, TOUCH);
}

(async () => {
    const report = { mode: BASELINE ? 'baseline' : 'full', site: SITE, at: new Date().toISOString(), views: {} };
    const browser = await chromium.launch({
        executablePath: EXE, headless: true,
        args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage'],
    });

    async function openVP(name) {
        const vp = VIEWPORTS[name];
        const ctx = await browser.newContext({
            viewport: { width: vp.width, height: vp.height },
            hasTouch: true, isMobile: vp.mobile, deviceScaleFactor: 1,
        });
        const page = await ctx.newPage();
        const errors = [];
        page.on('pageerror', (e) => errors.push(String(e)));
        page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
        await page.goto(SITE + '/?render=main', { waitUntil: 'domcontentloaded', timeout: 45000 });
        await page.waitForFunction(() => window.game && document.getElementById('game-canvas'), null, { timeout: 45000 });
        await page.waitForTimeout(BASELINE ? 1500 : 2500); // motor subir a cena
        return { ctx, page, errors };
    }

    // ---------- medição por viewport ----------
    for (const name of Object.keys(VIEWPORTS)) {
        const { ctx, page, errors } = await openVP(name);
        const m = await measure(page);
        m.consoleErrors = errors.filter((e) => !/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
        report.views[name] = m;
        await page.screenshot({ path: `${SHOTS}/${BASELINE ? 'antes' : 'depois'}-${name}.png` });
        const summary = {
            viewport: `${m.viewport.w}x${m.viewport.h}`, canvas: m.canvas ? `${m.canvas.w}x${m.canvas.h}` : null,
            meshes: m.meshes, noScroll: m.page.scrollW <= m.page.clientW && m.page.scrollH <= m.page.clientH,
            overlaps: m.overlaps.length, smallTargets: m.smallTouchTargets,
            hudVisible: Object.keys(m.rects),
        };
        console.log(`[${name}] ` + JSON.stringify(summary));
        if (!BASELINE) {
            if (name === 'portrait' || name === 'landscape') {
                ok(`${name}: canvas == viewport`, !!m.canvas && Math.abs(m.canvas.w - m.viewport.w) <= 1 && Math.abs(m.canvas.h - m.viewport.h) <= 1 && m.canvas.x === 0 && m.canvas.y === 0);
                ok(`${name}: sem rolagem`, summary.noScroll);
                ok(`${name}: cena ativa`, m.meshes > 0, 'malhas=' + m.meshes);
                ok(`${name}: sem sobreposição de HUD`, m.overlaps.length === 0, m.overlaps.slice(0, 3).join(' | '));
            }
            if (name === 'portrait') {
                // tela limpa: só sticks, A/B e botão de menu visíveis
                const clean = !m.rects['.ultima-status'] && !m.rects['.workshop-panel'] && !m.rects['.ultima-panel'] && !m.rects['.ultima-chat-log'] && !!m.rects['#ultima-menu-fab'];
                ok('retrato: menus ocultos por padrão (só menu fab + controles)', clean, Object.keys(m.rects).join(','));
                ok('retrato: sticks ≥112 e A ≥68/B ≥56',
                    m.touch['#move-stick'].w >= 112 && m.touch['#move-stick'].h >= 112 && m.touch['#look-stick'].w >= 112 &&
                    m.touch['#touch-action'].w >= 68 && m.touch['#touch-cancel'].w >= 56,
                    JSON.stringify(m.touch));
                // rotação no meio da sessão
                await page.setViewportSize({ width: 844, height: 390 });
                await page.waitForTimeout(1200);
                const m2 = await measure(page);
                ok('retrato→paisagem: canvas acompanha', Math.abs(m2.canvas.w - 844) <= 1 && Math.abs(m2.canvas.h - 390) <= 1, JSON.stringify(m2.canvas));
                ok('retrato→paisagem: sem sobreposição', m2.overlaps.length === 0, m2.overlaps.slice(0, 3).join(' | '));
                await page.screenshot({ path: `${SHOTS}/depois-rotate-landscape.png` });
                report.views['portrait→landscape'] = m2;
            }
        }
        await ctx.close();
    }

    // ---------- interações (só modo full) ----------
    if (!BASELINE) {
        // mobile retrato: sticks, A abre diálogo perto da Guardiã, B fecha
        {
            const { ctx, page, errors } = await openVP('portrait');
            const p0 = await page.evaluate(PLAYER_JS);
            const stick = await page.locator('#move-stick').boundingBox();
            if (stick) {
                const cx = stick.x + stick.width / 2, cy = stick.y + stick.height / 2;
                await page.touchscreen.tap(cx, cy).catch(() => {});
                const t = await page.touchscreen; // noop para existir
                await page.evaluate(async ({ cx, cy }) => {
                    const el = document.getElementById('move-stick');
                    const fire = (type, x, y) => el.dispatchEvent(new PointerEvent(type, {
                        pointerId: 9, pointerType: 'touch', isPrimary: true, bubbles: true,
                        clientX: x, clientY: y, buttons: 1, composed: true,
                    }));
                    fire('pointerdown', cx, cy);
                    for (let i = 1; i <= 12; i++) fire('pointermove', cx, cy - 6 * i);
                    fire('pointerup', cx, cy - 72);
                    fire('lostpointercapture', cx, cy - 72);
                }, { cx, cy });
                await page.waitForTimeout(2500);
            }
            const p1 = await page.evaluate(PLAYER_JS);
            const moved = p0 && p1 && Math.hypot(p1.x - p0.x, p1.z - p0.z) > 1;
            ok('retrato: arrastar stick de andar move o jogador (>1m)', !!moved,
                p0 && p1 ? `${p0.x},${p0.z} → ${p1.x},${p1.z} (${p1.src})` : 'posição indisponível');

            const look = await page.locator('#look-stick').boundingBox();
            if (look) {
                const cx = look.x + look.width / 2, cy = look.y + look.height / 2;
                const y0 = await page.evaluate(YAW_JS);
                await page.evaluate(async ({ cx, cy }) => {
                    const el = document.getElementById('look-stick');
                    const fire = (type, x, y) => el.dispatchEvent(new PointerEvent(type, {
                        pointerId: 7, pointerType: 'touch', isPrimary: true, bubbles: true,
                        clientX: x, clientY: y, buttons: 1, composed: true,
                    }));
                    fire('pointerdown', cx, cy);
                    for (let i = 1; i <= 10; i++) fire('pointermove', cx + 7 * i, cy);
                    fire('pointerup', cx + 70, cy);
                    fire('lostpointercapture', cx + 70, cy);
                }, { cx, cy });
                await page.waitForTimeout(1200);
                const y1 = await page.evaluate(YAW_JS);
                const yawMoved = y0 !== null && y1 !== null && Math.abs(y1 - y0) > 0.02;
                ok('retrato: arrastar stick de câmera muda o yaw', !!yawMoved, `${y0} → ${y1}`);
            } else fail('retrato: #look-stick não encontrado');

            // perto da Guardiã: A abre diálogo; sticks somem; B fecha
            await page.evaluate(() => window.game.teleportToGuardian && window.game.teleportToGuardian());
            await page.waitForTimeout(800);
            await page.locator('#touch-action').tap({ timeout: 8000 }).catch(() => page.evaluate(() => document.getElementById('touch-action').click()));
            const dialog = await page.waitForSelector('.ultima-dialog', { timeout: 15000, state: 'visible' }).catch(() => null);
            ok('retrato: A perto da Guardiã abre o diálogo', !!dialog);
            if (dialog) {
                const dm = await measure(page);
                report.views['portrait-dialog'] = dm;
                ok('diálogo: sticks ocultos enquanto aberto', !dm.touch['#move-stick'] && !dm.touch['#look-stick']);
                ok('diálogo: A/B continuam ≥68/56', !!dm.touch['#touch-action'] && dm.touch['#touch-action'].w >= 68 && !!dm.touch['#touch-cancel'] && dm.touch['#touch-cancel'].w >= 56);
                const line = await page.evaluate(() => { const el = document.querySelector('.ultima-dialog__line, .ultima-dialog'); return el ? el.textContent.trim().slice(0, 80) : ''; });
                ok('diálogo: texto visível/legível', line.length > 3, JSON.stringify(line));
                const inputFs = await page.evaluate(() => { const el = document.getElementById('dialog-input'); return el ? parseFloat(getComputedStyle(el).fontSize) : 0; });
                ok('diálogo: campo ≥16px (sem auto-zoom iOS)', inputFs >= 16, inputFs + 'px');
                // teclado virtual simulado: altura encolhe p/ 500
                await page.setViewportSize({ width: 390, height: 500 });
                await page.waitForTimeout(700);
                const dk = await measure(page);
                report.views['portrait-dialog-keyboard'] = dk;
                const vis = !!dk.rects['.ultima-dialog'] && dk.rects['.ultima-dialog'].y + dk.rects['.ultima-dialog'].h <= 500 + 1;
                ok('diálogo com teclado (390x500): continua na tela', vis, JSON.stringify(dk.rects['.ultima-dialog']));
                await page.setViewportSize({ width: 390, height: 844 });
                await page.waitForTimeout(600);
                await page.locator('#touch-cancel').tap({ timeout: 8000 }).catch(() => page.evaluate(() => document.getElementById('touch-cancel').click()));
                const closed = await page.waitForSelector('.ultima-dialog', { timeout: 8000, state: 'hidden' }).catch(() => null);
                ok('retrato: B fecha o diálogo', !!closed);
                await page.screenshot({ path: `${SHOTS}/depois-dialog-fechado.png` });
            }

            // menu abre/fecha por toque; folha cabe e rola; alvos ≥48 dentro
            await page.locator('#ultima-menu-fab').tap({ timeout: 8000 }).catch(() => page.evaluate(() => document.getElementById('ultima-menu-fab') && document.getElementById('ultima-menu-fab').click()));
            const menu = await page.waitForSelector('.ultima-menu--open, .ultima-menu[open]', { timeout: 8000, state: 'visible' }).catch(() => null);
            ok('retrato: toque no fab abre o menu', !!menu);
            if (menu) {
                await page.waitForTimeout(400);
                const mm = await measure(page);
                report.views['portrait-menu'] = mm;
                const fits = mm.rects['.ultima-menu'] && mm.rects['.ultima-menu'].h <= 844 && mm.rects['.ultima-menu'].w <= 390;
                ok('menu: cabe no viewport', !!fits, JSON.stringify(mm.rects['.ultima-menu']));
                ok('menu: alvos ≥48px dentro', (mm.smallTouchTargets || []).length === 0, (mm.smallTouchTargets || []).join(' | ').slice(0, 160));
                const scrollable = await page.evaluate(() => { const el = document.querySelector('.ultima-menu'); return el ? el.scrollHeight > el.clientHeight + 8 : false; });
                console.log('info  menu rola internamente: ' + scrollable);
                await page.screenshot({ path: `${SHOTS}/depois-menu-aberto.png` });
                const closer = page.locator('#ultima-menu-fab, .ultima-menu summary, .ultima-menu-backdrop').first();
                await closer.tap({ timeout: 8000 }).catch(() => page.evaluate(() => document.getElementById('ultima-menu-fab').click()));
                const shut = await page.waitForSelector('.ultima-menu--open, .ultima-menu[open]', { timeout: 8000, state: 'hidden' }).catch(() => null);
                ok('menu: fecha por toque e volta à tela limpa', !!shut);
            }
            const rel = errors.filter((e) => !/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
            ok('retrato: console sem erros', rel.length === 0, rel.slice(0, 3).join(' | ').slice(0, 200));
            await ctx.close();
        }

        // desktop: regressão
        {
            const { ctx, page, errors } = await openVP('desktop');
            const m = await measure(page);
            report.views['desktop'] = m;
            ok('desktop: sticks ocultos', !m.touch['#move-stick'] && !m.touch['#touch-action'], JSON.stringify(Object.keys(m.touch)));
            const p0 = await page.evaluate(PLAYER_JS);
            await page.keyboard.down('KeyW');
            await page.waitForTimeout(2200);
            await page.keyboard.up('KeyW');
            const p1 = await page.evaluate(PLAYER_JS);
            ok('desktop: WASD anda', p0 && p1 && Math.hypot(p1.x - p0.x, p1.z - p0.z) > 1, p0 && p1 ? `${p0.x},${p0.z} → ${p1.x},${p1.z}` : '');
            const y0 = await page.evaluate(YAW_JS);
            await page.mouse.move(640, 300); await page.mouse.down();
            await page.mouse.move(760, 300, { steps: 8 });
            await page.mouse.up();
            await page.waitForTimeout(600);
            const y1 = await page.evaluate(YAW_JS);
            ok('desktop: câmera com mouse', y0 !== null && y1 !== null && Math.abs(y1 - y0) > 0.01, `${y0} → ${y1}`);
            await page.evaluate(() => window.game.teleportToGuardian && window.game.teleportToGuardian());
            await page.waitForTimeout(600);
            await page.keyboard.press('KeyE');
            const d = await page.waitForSelector('.ultima-dialog', { timeout: 12000, state: 'visible' }).catch(() => null);
            ok('desktop: E abre diálogo', !!d);
            if (d) { await page.keyboard.press('Escape'); const c = await page.waitForSelector('.ultima-dialog', { timeout: 8000, state: 'hidden' }).catch(() => null); ok('desktop: Esc fecha diálogo', !!c); }
            const tog = await page.locator('.ultima-panel-toggle').click({ timeout: 8000 }).catch(() => null);
            const panel = await page.waitForSelector('.ultima-panel', { timeout: 8000, state: 'visible' }).catch(() => null);
            ok('desktop: toggle abre painel', !!panel);
            await page.screenshot({ path: `${SHOTS}/depois-desktop-panel.png` });
            const rel = errors.filter((e) => !/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
            ok('desktop: console sem erros', rel.length === 0, rel.slice(0, 3).join(' | ').slice(0, 200));
            await ctx.close();
        }
    }

    fs.writeFileSync(path.join(__dirname, '..', 'decisions', `ui-mobile-${BASELINE ? 'baseline' : 'after'}.json`), JSON.stringify(report, null, 2));
    console.log('relatório salvo em decisions/ui-mobile-' + (BASELINE ? 'baseline' : 'after') + '.json');
    await browser.close();
})().catch((e) => { fail(e && e.stack || e); process.exit(1); });
