// Integrações do navegador para a fila de diálogos do Blazor.
(function () {
    'use strict';

    let bridge;
    const fallbackApi = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';

    function onLeaving() {
        bridge?.invokeMethodAsync('OnDialogFlush', true, true).catch(() => {});
    }
    window.addEventListener('pagehide', onLeaving);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') { onLeaving(); }
    });
    window.addEventListener('ultima-dialog', ev => {
        const turn = ev.detail;
        if (turn) { game.dialogLog.turn(turn.npc, turn.player, turn.reply, turn.source, turn.ms).catch(() => {}); }
    });

    window.game.dialogLog = {
        init(dotnet) { bridge = dotnet; },
        context() {
            const query = new URLSearchParams(location.search);
            const config = window.ULTIMA_DIALOG_LOG || {};
            return {
                api: config.api || query.get('api') || window.ULTIMA_API || game.net?.info()?.api || fallbackApi,
                endpoint: config.endpoint || null,
                enabled: config.enabled !== false && query.get('dialogos') !== '0',
                intervalMs: Math.trunc(Number(config.intervalMs)) || null,
                batchSize: Math.trunc(Number(config.batchSize)) || null,
                maxQueue: Math.trunc(Number(config.maxQueue)) || null
            };
        },
        turn(npc, player, reply, source, ms) {
            return bridge?.invokeMethodAsync('OnDialogTurn', npc ?? '', player ?? '', reply ?? '', source ?? '',
                Math.max(0, Math.min(600000, Math.round(Number(ms) || 0)))) ?? Promise.resolve();
        },
        flush(options) { return bridge?.invokeMethodAsync('OnDialogFlush', !!options?.force, !!options?.keepalive); },
        debug() { return bridge?.invokeMethodAsync('OnDialogDebug'); },
        async send(url, batch, keepalive) {
            const response = await fetch(url, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(batch), keepalive
            });
            if (!response.ok) { throw new Error('http ' + response.status); }
            return response.json();
        },
        beacon(url, batch) {
            try { return navigator.sendBeacon(url, new Blob([JSON.stringify(batch)], { type: 'text/plain' })); }
            catch { return false; }
        }
    };
})();
