const assert = require('node:assert/strict');
const { chromium } = require('playwright-core');
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';

(async () => {
    const browser = await chromium.launch({
        executablePath: process.env.ULTIMA_CHROMIUM,
        args: (process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)
    });
    try {
        const page = await browser.newPage({ viewport: { width: 1200, height: 720 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        if (process.env.ULTIMA_DIAGNOSTIC) page.on('console', message => console.log(message.type(), message.text().slice(0, 900)));
        const url = new URL(site);
        url.search = 'live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999';
        await page.goto(url.href);
        if (process.env.ULTIMA_BUILD)
            assert.equal(await page.locator('meta[name=ultima-build]').getAttribute('content'), process.env.ULTIMA_BUILD);
        const panel = page.getByRole('button', { name: 'Abrir painel do jogo' });
        const journal = page.getByRole('dialog', { name: 'Diário' });
        await panel.click();
        await page.getByRole('button', { name: 'Diário', exact: true }).click();
        await journal.getByText('Suas conversas com personagens aparecem aqui.').waitFor();
        const card = journal.locator('.ultima-card--glass');
        const glass = await card.evaluate(node => getComputedStyle(node).backgroundColor);
        await journal.getByRole('button', { name: 'Mais contraste' }).click();
        assert.notEqual(await card.evaluate(node => getComputedStyle(node).backgroundColor), glass);
        await journal.getByRole('button', { name: 'Tema padrão' }).waitFor();
        await journal.getByRole('button', { name: 'Fechar · Esc' }).click();
        await journal.waitFor({ state: 'hidden' });

        await page.waitForFunction(() => window.game?.mode === 'worker');
        assert.equal(await page.evaluate(() => game.teleportToGuardian()), true);
        await page.locator('#game-canvas').focus();
        // O checkpoint de proximidade é atualizado no tick do mundo.
        await page.locator('.ultima-hint').getByText('para conversar').waitFor();
        await page.keyboard.press('e');
        const conversation = page.getByRole('dialog', { name: 'Conversa com a Guardiã' });
        await conversation.waitFor({ timeout: 10000 }).catch(async error => {
            console.log(await page.getByRole('dialog').all().then(items => Promise.all(items.map(item => item.getAttribute('aria-label')))));
            console.log(await page.locator('.ultima-hint').allTextContents(), errors);
            throw error;
        });
        assert.match(await conversation.getAttribute('class'), /ultima-window--contrast/);
        const line = conversation.locator('.ultima-dialog__line');
        const first = await line.innerText();
        await conversation.getByRole('button', { name: 'Continuar · E/A' }).click();
        await page.waitForFunction(initial => document.querySelector('.ultima-dialog__line')?.textContent !== initial, first);
        await conversation.getByRole('textbox', { name: 'Escreva para a Guardiã' }).fill('Onde fica o jardim?');
        await conversation.getByRole('button', { name: 'Enviar para a Guardiã' }).click();
        await conversation.getByText(/origem da resposta/).waitFor();
        const reply = await line.innerText();
        await conversation.getByRole('button', { name: 'Fechar · Esc' }).click();
        await conversation.waitFor({ state: 'hidden' });
        await panel.click();
        await page.getByRole('button', { name: 'Diário', exact: true }).click();
        await journal.getByText(reply, { exact: false }).waitFor();
        assert.deepEqual(errors, []);
        console.log({ journal: true, dialogue: true, contrast: true, reply });
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
