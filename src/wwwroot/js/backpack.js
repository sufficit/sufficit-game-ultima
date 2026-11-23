// Cada entrada do IndexedDB guarda o recurso e a marca de coleta na mesma transação.
(function () {
    'use strict';

    const storeName = 'collected';
    let database;
    let bridge;
    let appliedCount = -1;
    const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('ultima-backpack') : null;

    function open() {
        return database ??= new Promise((resolve, reject) => {
            const request = indexedDB.open('ultima-backpack', 1);
            request.onupgradeneeded = () => request.result.createObjectStore(storeName, { keyPath: 'id' });
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        }).catch(error => { database = null; throw error; });
    }

    async function synchronize() {
        const db = await open();
        const entries = await new Promise((resolve, reject) => {
            const request = db.transaction(storeName).objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        // Operações em outras abas só acrescentam entradas. Uma leitura atrasada
        // nunca deve substituir um snapshot mais recente.
        if (entries.length < appliedCount || !bridge) { return; }
        appliedCount = entries.length;
        const counts = Object.create(null);
        for (const entry of entries) { counts[entry.item] = (counts[entry.item] || 0) + 1; }
        game.setCollectedResources(entries);
        await bridge.invokeMethodAsync('OnBackpackChanged', counts);
    }

    function report(text) {
        return bridge?.invokeMethodAsync('OnBackpackStatus', text).catch(() => {});
    }

    async function refresh() {
        try { await synchronize(); }
        catch { await report('A mochila não conseguiu acessar o armazenamento deste navegador.'); }
    }

    async function collect(entry) {
        try {
            const db = await open();
            await new Promise((resolve, reject) => {
                const tx = db.transaction(storeName, 'readwrite');
                const add = tx.objectStore(storeName).add(entry);
                tx.oncomplete = resolve;
                tx.onabort = () => reject(add.error || tx.error);
            });
        } catch (error) {
            if (error?.name === 'ConstraintError') {
                await refresh();
                await report('Esse recurso já foi coletado.');
            } else {
                await report('A coleta não pôde ser salva. O recurso continua no cenário.');
            }
            return;
        }
        channel?.postMessage('changed');
        await refresh();
        await report('+1 ' + entry.item);
    }

    if (channel) { channel.onmessage = () => { if (bridge) { void refresh(); } }; }
    window.addEventListener('focus', () => { if (bridge) { void refresh(); } });
    window.game.backpack = {
        init(dotnet) { bridge = dotnet; appliedCount = -1; return refresh(); },
        collect
    };
})();
