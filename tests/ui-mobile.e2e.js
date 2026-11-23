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

const SITE = (process.argv[2] || 'http://127.0.0.1:8092').replace(/\/+$/, ''); // sem barra final: evita "//?render=main"
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

// ---- funções avaliadas na página (passadas por referência ao evaluate) -----------
function MEASURE(args) {
    const sel = args.sel, touchSel = args.touchSel;
    const visible = (el) => {
        if (!el) return false;
        const r = el.getBoundingClientRect();
        if (r.width < 1 || r.height < 1) return false;
        const s = getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    };
    const rect = (q) => {
        const el = document.querySelector(q);
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
    out.meshes = 0; // preenchido fora: 1 quando o motor responde cameraState (query ao core)
    const small = [];
    try {
        const els = document.querySelectorAll('button, input, select, textarea, a[href], summary, [role="button"]');
        for (const el of els) {
            if (!visible(el) || el.closest('.ultima-chat-log')) continue;
            const r = el.getBoundingClientRect();
            if (r.width < 48 || r.height < 48) {
                small.push((el.id ? '#' + el.id : el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.split(' ')[0] : '')) + ' ' + r.width.toFixed(0) + 'x' + r.height.toFixed(0));
            }
        }
    } catch (e) {}
    out.smallTouchTargets = small.slice(0, 12);
    const overlaps = [];
    const names = [...sel, ...touchSel];
    const rects = names.map((n) => ({ n, r: rect(n) })).filter((o) => o.r);
    for (let i = 0; i < rects.length; i++) for (let j = i + 1; j < rects.length; j++) {
        // polegar vive DENTRO do próprio stick: pares pai→filho não são sobreposição
        const elI = document.querySelector(rects[i].n), elJ = document.querySelector(rects[j].n);
        if ((elI && elJ && (elI.contains(elJ) || elJ.contains(elI)))) continue;
        const a = rects[i].r, b = rects[j].r;
        const ix = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
        const iy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
        if (ix > 2 && iy > 2) overlaps.push(rects[i].n + ' ∩ ' + rects[j].n + ' ' + ix.toFixed(0) + 'x' + iy.toFixed(0));
    }
    out.overlaps = overlaps;
    return out;
}

async function PLAYER() {
    try {
        const p = await window.game.playerState();
        if (p && isFinite(p.x)) return { x: +p.x.toFixed(2), z: +p.z.toFixed(2), ry: +(p.ry || 0).toFixed(3), src: 'game.playerState' };
    } catch (e) {}
    try {
        const t = (document.querySelector('.ultima-status') || {}).textContent || '';
        const m = t.match(/pos \((-?[\d.,]+), (-?[\d.,]+)\)/);
        if (m) return { x: parseFloat(m[1].replace(',', '.')), z: parseFloat(m[2].replace(',', '.')), src: 'status' };
    } catch (e) {}
    return null;
}

async function YAW() {
    try {
        const c = await window.game.cameraState();
        if (c && isFinite(c.alpha)) return +c.alpha.toFixed(3);
    } catch (e) {}
    return null;
}
async function CAMERA() {
    try { const c = await window.game.cameraState(); return c && isFinite(c.alpha) ? { alpha: +c.alpha.toFixed(3), beta: +c.beta.toFixed(3), radius: +c.radius.toFixed(2) } : null; } catch (e) { return null; }
}
async function RENDER() {
    try { const r = await window.game.renderSize(); return r ? { width: r.width, height: r.height, canvasW: r.canvasW, canvasH: r.canvasH } : null; } catch (e) { return null; }
}

const measure = (page) => page.evaluate(MEASURE, { sel: HUD, touchSel: TOUCH });

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
            hasTouch: !!vp.mobile, isMobile: !!vp.mobile, deviceScaleFactor: 1,
        });
        const page = await ctx.newPage();
        const errors = [];
        page.on('pageerror', (e) => errors.push(String(e)));
        page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
        await page.goto(SITE + '/?render=main', { waitUntil: 'domcontentloaded', timeout: 45000 });
        // Boot por polling (page.evaluate), nao waitForFunction: no headless com
        // swiftshader o rAF engasga (FPS ~1-2) e a espera por animation frame
        // estoura o timeout mesmo com o jogo pronto em ~5s (comprovado por sonda).
        let booted = false;
        for (let t = 0; t < 45; t++) {
            booted = await page.evaluate(() => !!(window.game && document.getElementById('game-canvas'))).catch(() => false);
            if (booted) break;
            await page.waitForTimeout(1000);
        }
        if (!booted) throw new Error('boot: window.game/#game-canvas indisponiveis apos 45s');
        // Motor subir a cena: em headless o FPS é ~1-2, então espera a câmera responder
        // (cameraState) em vez de sono fixo — evita "cena ativa n/d" em qualquer viewport.
        for (let t = 0; t < 45; t++) {
            const c = await page.evaluate(() => (window.game.cameraState ? window.game.cameraState() : null)).catch(() => null);
            if (c && isFinite(c.alpha)) break;
            await page.waitForTimeout(1000);
        }
        await page.waitForTimeout(400);
        return { ctx, page, errors };
    }

    // ---------- medição por viewport ----------
    for (const name of Object.keys(VIEWPORTS)) {
        const { ctx, page, errors } = await openVP(name);
        const m = await measure(page);
        m.consoleErrors = errors.filter((e) => !/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
        m.camera = await page.evaluate(CAMERA); m.render = await page.evaluate(RENDER); m.meshes = m.camera ? 1 : 0;
        console.log(`info  ${name}: render ${m.render ? m.render.width + 'x' + m.render.height + ' (buffer ' + m.render.canvasW + 'x' + m.render.canvasH + ')' : 'n/d'}`);
        report.views[name] = m;
        try { await page.screenshot({ path: `${SHOTS}/${BASELINE ? 'antes' : 'depois'}-${name}.png`, timeout: 30000 }); }
        catch (e) { console.log('info  captura ' + name + ' falhou (sem bloquear): ' + String(e).split('\n')[0].slice(0, 120)); }
        const summary = {
            viewport: `${m.viewport.w}x${m.viewport.h}`, canvas: m.canvas ? `${m.canvas.w}x${m.canvas.h}` : null,
            meshes: m.meshes, noScroll: m.page.scrollW <= m.page.clientW && m.page.scrollH <= m.page.clientH,
            overlaps: m.overlaps.length, smallTargets: m.smallTouchTargets,
            hudVisible: Object.keys(m.rects),
        };
        console.log(`[${name}] ` + JSON.stringify(summary));
        if (!BASELINE && (name === 'portrait' || name === 'landscape')) {
            ok(`${name}: canvas == viewport`, !!m.canvas && Math.abs(m.canvas.w - m.viewport.w) <= 1 && Math.abs(m.canvas.h - m.viewport.h) <= 1 && m.canvas.x === 0 && m.canvas.y === 0);
            ok(`${name}: sem rolagem`, summary.noScroll);
            ok(`${name}: cena ativa`, m.meshes > 0, 'malhas=' + m.meshes);
            ok(`${name}: sem sobreposição de HUD`, m.overlaps.length === 0, m.overlaps.slice(0, 3).join(' | '));
        }
        if (!BASELINE && name === 'portrait') {
            const clean = !m.rects['.ultima-status'] && !m.rects['.workshop-panel'] && !m.rects['.ultima-panel'] && !m.rects['.ultima-chat-log'] && !!m.rects['#ultima-menu-fab'];
            ok('retrato: menus ocultos por padrão (só fab + controles)', clean, Object.keys(m.rects).join(','));
            const st = m.touch['#move-stick'], lk = m.touch['#look-stick'], a = m.touch['#touch-action'], b = m.touch['#touch-cancel'];
            ok('retrato: sticks ≥112 e A ≥68/B ≥56', !!st && st.w >= 112 && st.h >= 112 && !!lk && lk.w >= 112 && !!a && a.w >= 68 && !!b && b.w >= 56, JSON.stringify(m.touch));
            // rotação no meio da sessão
            await page.setViewportSize({ width: 844, height: 390 });
            await page.waitForTimeout(1200);
            const m2 = await measure(page);
            ok('retrato→paisagem: canvas acompanha', !!m2.canvas && Math.abs(m2.canvas.w - 844) <= 1 && Math.abs(m2.canvas.h - 390) <= 1, JSON.stringify(m2.canvas));
            ok('retrato→paisagem: sem sobreposição', m2.overlaps.length === 0, m2.overlaps.slice(0, 3).join(' | '));
            await page.screenshot({ path: `${SHOTS}/depois-rotate-landscape.png` });
            report.views['portrait→landscape'] = m2;
        }
        await ctx.close();
    }

    // ---------- interações (só modo full) ----------
    if (!BASELINE) {
        // mobile retrato: sticks, A abre diálogo perto da Guardiã, B fecha, menu
        {
            const { ctx, page, errors } = await openVP('portrait');
            // O arrasto tem que FICAR SEGURADO por vários frames: o core amostra o
            // stick por frame (headless ~1-2 fps); disparar tudo na mesma task não move nada.
            const fireAt = (id, type, x, y) => page.evaluate(({ id, type, x, y }) => {
                const el = document.getElementById(id);
                el.dispatchEvent(new PointerEvent(type, {
                    pointerId: 9, pointerType: 'touch', isPrimary: true, bubbles: true, composed: true,
                    clientX: x, clientY: y, buttons: 1,
                }));
            }, { id, type, x, y });
            const stickDown = async (id, dx, dy, steps) => {
                const c = await page.evaluate((id) => {
                    const el = document.getElementById(id);
                    const r = el.getBoundingClientRect();
                    return { cx: r.x + r.width / 2, cy: r.y + r.height / 2 };
                }, id);
                await fireAt(id, 'pointerdown', c.cx, c.cy);
                for (let i = 1; i <= steps; i++) {
                    await fireAt(id, 'pointermove', c.cx + (dx * i) / steps, c.cy + (dy * i) / steps);
                    await page.waitForTimeout(200);
                }
                return { id: id, cx: c.cx + dx, cy: c.cy + dy };
            };
            const stickUp = async (end) => {
                await fireAt(end.id, 'pointerup', end.cx, end.cy);
                await fireAt(end.id, 'lostpointercapture', end.cx, end.cy);
            };

            const p0 = await page.evaluate(PLAYER);
            // Segurado e amostrado: em headless (render por software) o motor roda a ~1 fps e a
            // simulação avança no máximo 50 ms por frame — andar 1 m leva vários frames.
            let end = await stickDown('move-stick', 0, -72, 6);
            let p1 = p0;
            for (let i = 0; i < 20; i++) {
                await page.waitForTimeout(1000);
                p1 = await page.evaluate(PLAYER);
                if (p0 && p1 && Math.hypot(p1.x - p0.x, p1.z - p0.z) > 1.5) break;
            }
            await stickUp(end);
            ok('retrato: arrastar stick de andar move o jogador (>1m)',
                !!(p0 && p1 && Math.hypot(p1.x - p0.x, p1.z - p0.z) > 1),
                p0 && p1 ? `${p0.x},${p0.z} → ${p1.x},${p1.z} (${p1.src})` : 'posição indisponível');

            const y0 = await page.evaluate(YAW);
            end = await stickDown('look-stick', 70, 0, 5);
            let y1 = y0;
            for (let i = 0; i < 15; i++) {
                await page.waitForTimeout(1000);
                y1 = await page.evaluate(YAW);
                if (y0 !== null && y1 !== null && Math.abs(y1 - y0) > 0.15) break;
            }
            await stickUp(end);
            ok('retrato: arrastar stick de câmera muda o yaw', y0 !== null && y1 !== null && Math.abs(y1 - y0) > 0.02, `${y0} → ${y1}`);

            // perto da Guardiã: A abre diálogo; sticks somem; B fecha
            await page.evaluate(() => window.game.teleportToGuardian && window.game.teleportToGuardian());
            // _near só vira true quando checkNear roda num frame (~1-2 fps): esperar a dica.
            const nearHint = await page.waitForSelector('.ultima-hint', { timeout: 30000, state: 'visible' }).catch(() => null);
            ok('retrato: dica de proximidade após o teleport', !!nearHint);
            // A e B respondem a pointerdown (game-core.js): um .click() sintético nunca os
            // dispara, e o fallback escondia isso. Toque real, sem fallback.
            // A escuta pointerdown (game-core.js). No run 5 o locator.tap travou após a
            // actionability (toque nunca confirmado a 1-2 fps): disparar pointerdown direto,
            // a mesma via dos sticks — comprovada neste ambiente.
            {
                const c = await page.evaluate(() => { const r = document.getElementById('touch-action').getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; });
                await fireAt('touch-action', 'pointerdown', c.x, c.y);
                await fireAt('touch-action', 'pointerup', c.x, c.y);
            }
            const dialog = await page.waitForSelector('.ultima-dialog', { timeout: 30000, state: 'visible' }).catch(() => null);
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
                const rct = dk.rects['.ultima-dialog'];
                ok('diálogo com teclado (390x500): continua na tela', !!rct && rct.y + rct.h <= 501, JSON.stringify(rct));
                await page.setViewportSize({ width: 390, height: 844 });
                await page.waitForTimeout(2500); // render=main: o resize do motor trava a thread principal por dezenas de s (GPU stall)
                {
                    const c = await page.evaluate(() => { const r = document.getElementById('touch-cancel').getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; });
                    await fireAt('touch-cancel', 'pointerdown', c.x, c.y);
                    await fireAt('touch-cancel', 'pointerup', c.x, c.y);
                }
                const closed = await page.waitForSelector('.ultima-dialog', { timeout: 90000, state: 'hidden' }).catch(() => null);
                ok('retrato: B fecha o diálogo', !!closed);
                await page.screenshot({ path: `${SHOTS}/depois-dialog-fechado.png` }).catch((e) => console.log('info  captura depois-dialog-fechado falhou (sem bloquear): ' + (e && e.message || '').split('\n')[0]));
            }

            // menu abre/fecha por toque; folha cabe e rola; alvos ≥48 dentro
            await page.evaluate(() => { const el = document.getElementById('ultima-menu-fab'); if (el) el.click(); });
            const menu = await page.waitForSelector('.game-shell.ultima-menu-open .ultima-menu', { timeout: 20000, state: 'visible' }).catch(() => null);
            ok('retrato: toque no fab abre o menu', !!menu);
            if (menu) {
                await page.waitForTimeout(400);
                const mm = await measure(page);
                report.views['portrait-menu'] = mm;
                const mr = mm.rects['.ultima-menu'];
                ok('menu: cabe no viewport', !!mr && mr.h <= 844 && mr.w <= 390 && mr.x >= -1 && mr.y >= -1, JSON.stringify(mr));
                ok('menu: alvos ≥48px dentro', (mm.smallTouchTargets || []).length === 0, (mm.smallTouchTargets || []).join(' | ').slice(0, 160));
                const scrollable = await page.evaluate(() => { const el = document.querySelector('.ultima-menu'); if (!el) return false; return el.scrollHeight > el.clientHeight + 8 || getComputedStyle(el).overflowY === 'auto'; });
                console.log('info  menu rola internamente: ' + scrollable);
                await page.screenshot({ path: `${SHOTS}/depois-menu-aberto.png` }).catch((e) => console.log('info  captura depois-menu-aberto falhou (sem bloquear): ' + (e && e.message || '').split('\n')[0]));
                await page.evaluate(() => { const el = document.querySelector('.ultima-menu__close, .ultima-menu-backdrop'); if (el) el.click(); });
                const shut = await page.waitForSelector('.game-shell.ultima-menu-open .ultima-menu', { timeout: 90000, state: 'hidden' }).catch(() => null);
                ok('menu: fecha por toque e volta à tela limpa', !!shut);
            }
            const rel = errors.filter((e) => !/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
            ok('retrato: console sem erros', rel.length === 0, rel.slice(0, 3).join(' | ').slice(0, 200));
            await ctx.close();
        }

        // desktop: regressão + comparação com a linha de base
        {
            const { ctx, page, errors } = await openVP('desktop');
            const m = await measure(page);
            report.views['desktop'] = m;
            ok('desktop: sticks ocultos', !m.touch['#move-stick'] && !m.touch['#touch-action'], JSON.stringify(Object.keys(m.touch)));
            try {
                const bl = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'decisions', 'ui-mobile-baseline.json'), 'utf8')).views.desktop;
                for (const k of ['.ultima-status', '.ultima-panel-toggle']) {
                    const a = bl.rects[k], b = m.rects[k];
                    const geo = !!a && !!b && Math.abs(a.x - b.x) <= 1 && Math.abs(a.y - b.y) <= 1 && Math.abs(a.w - b.w) <= 1;
                    const dh = (a && b) ? Math.abs(a.h - b.h) : -1;
                    // .ultima-status: altura varia com o texto dinamico (rede/jogadores) — geometria igual, Δh registrado
                    ok('desktop: retângulo igual à linha de base ' + k, geo && dh <= (k === '.ultima-status' ? 60 : 1), JSON.stringify(b) + ' vs ' + JSON.stringify(a) + ' Δh=' + dh);
                }
                console.log(`info  canvas desktop: linha de base ${bl.canvas && bl.canvas.w + 'x' + bl.canvas.h} (cadeia 2:1, corrigida na origin/main 5208961) -> agora ${m.canvas && m.canvas.w + 'x' + m.canvas.h}`);
            } catch (e) { console.log('info  linha de base desktop indisponível: ' + e.message); }
            const p0 = await page.evaluate(PLAYER);
            await page.keyboard.down('KeyW');
            let p1 = p0;   // mesmo motivo do stick: segurar até a distância aparecer
            for (let i = 0; i < 20; i++) {
                await page.waitForTimeout(1000);
                p1 = await page.evaluate(PLAYER);
                if (p0 && p1 && Math.hypot(p1.x - p0.x, p1.z - p0.z) > 1.5) break;
            }
            await page.keyboard.up('KeyW');
            ok('desktop: WASD anda', !!(p0 && p1 && Math.hypot(p1.x - p0.x, p1.z - p0.z) > 1), p0 && p1 ? `${p0.x},${p0.z} → ${p1.x},${p1.z}` : '');
            const y0 = await page.evaluate(YAW);
            await page.mouse.move(640, 300); await page.mouse.down();
            await page.mouse.move(760, 300, { steps: 8 });
            await page.mouse.up();
            await page.waitForTimeout(600);
            const y1 = await page.evaluate(YAW);
            ok('desktop: câmera com mouse', y0 !== null && y1 !== null && Math.abs(y1 - y0) > 0.01, `${y0} → ${y1}`);
            await page.evaluate(() => window.game.teleportToGuardian && window.game.teleportToGuardian());
            await page.waitForSelector('.ultima-hint', { timeout: 30000, state: 'visible' }).catch(() => null);
            await page.waitForTimeout(300);
            await page.keyboard.press('KeyE');
            const d = await page.waitForSelector('.ultima-dialog', { timeout: 30000, state: 'visible' }).catch(() => null);
            ok('desktop: E abre diálogo', !!d);
            if (d) { await page.keyboard.press('Escape'); const c = await page.waitForSelector('.ultima-dialog', { timeout: 90000, state: 'hidden' }).catch(() => null); ok('desktop: Esc fecha diálogo', !!c); }
            await page.evaluate(() => { const el = document.querySelector('.ultima-panel-toggle button') || document.querySelector('.ultima-panel-toggle'); if (el) el.click(); }); // o OnClick e do button (SUIButton), nao do involumetro
            const panel = await page.waitForSelector('.ultima-panel', { timeout: 20000, state: 'visible' }).catch(() => null);
            ok('desktop: toggle abre painel', !!panel);
            await page.screenshot({ path: `${SHOTS}/depois-desktop-panel.png` }).catch((e) => console.log('info  captura depois-desktop-panel falhou (sem bloquear): ' + (e && e.message || '').split('\n')[0]));
            const rel = errors.filter((e) => !/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(e));
            ok('desktop: console sem erros', rel.length === 0, rel.slice(0, 3).join(' | ').slice(0, 200));
            await ctx.close();
        }
    }

    fs.writeFileSync(path.join(__dirname, '..', 'decisions', `ui-mobile-${BASELINE ? 'baseline' : 'after'}.json`), JSON.stringify(report, null, 2));
    console.log('relatório salvo em decisions/ui-mobile-' + (BASELINE ? 'baseline' : 'after') + '.json');
    await browser.close();
})().catch(async (e) => { fail(e && e.stack || e); try { await browser.close(); } catch (_) { } process.exit(1); });
