// Adaptador IndexedDB. A chave e o formato das entradas são mantidos entre versões.
(function () {
    'use strict';

    const storeName = 'collected';
    let database;
    let bridge;
    const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('ultima-backpack') : null;

    function open() {
        return database ??= new Promise((resolve, reject) => {
            const request = indexedDB.open('ultima-backpack', 1);
            request.onupgradeneeded = () => request.result.createObjectStore(storeName, { keyPath: 'id' });
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        }).catch(error => { database = null; throw error; });
    }

    async function read() {
        const db = await open();
        return new Promise((resolve, reject) => {
            const request = db.transaction(storeName).objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function add(entry) {
        const db = await open();
        const result = await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readwrite');
            const request = tx.objectStore(storeName).add(entry);
            tx.oncomplete = () => resolve('added');
            tx.onabort = () => {
                const error = request.error || tx.error;
                if (error?.name === 'ConstraintError') { resolve('duplicate'); }
                else { reject(error); }
            };
        });
        if (result === 'added') { channel?.postMessage('changed'); }
        return result;
    }

    function refresh() { bridge?.invokeMethodAsync('OnBackpackRefresh').catch(() => {}); }

    if (channel) { channel.onmessage = refresh; }
    window.addEventListener('focus', refresh);
    window.game.backpack = {
        init(dotnet) { bridge = dotnet; },
        collect(entry, point) { return bridge?.invokeMethodAsync('OnCollectResource', entry, point ?? null); },
        read, add
    };
})();
