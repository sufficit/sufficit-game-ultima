import { contextualize, memoryCommand } from './guardian-memory.mjs';
export { useMemoryPlugin } from './guardian-memory.mjs';

let database;
const attempts = new Map();
const sessions = 'sessions';

function open() {
    return database ??= new Promise((resolve, reject) => {
        const request = indexedDB.open('ultima-genius', 1);
        request.onupgradeneeded = () => request.result.createObjectStore(sessions);
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            const db = request.result;
            db.onversionchange = () => { db.close(); database = null; };
            resolve(db);
        };
    }).catch(error => { database = null; throw error; });
}

export function npcConversationId(npc) {
    const peer = game.net.info().id;
    let player = peer;
    try {
        const saved = localStorage.getItem('ultima-id');
        if (/^[0-9a-f]{8}$/.test(saved ?? '')) player = saved;
    } catch { /* Browser storage may be restricted. */ }
    return JSON.stringify(['player', player, npc]);
}

export function guardianConversationId() { return npcConversationId('guardia'); }

export async function load(id) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction(sessions, 'readwrite');
        const store = transaction.objectStore(sessions);
        const request = store.get(id);
        let snapshot = null;
        transaction.oncomplete = () => resolve(snapshot?.json ?? null);
        transaction.onabort = () => reject(transaction.error);
        request.onsuccess = () => {
            snapshot = request.result ?? null;
            if (snapshot || typeof id !== 'string') return;
            const [version, player, npc] = JSON.parse(id);
            if (version !== 'player') return;
            const peers = new Set([player, game.net.info().id]);
            const scan = store.openCursor();
            scan.onsuccess = () => {
                const cursor = scan.result;
                if (!cursor) {
                    if (snapshot) store.put(snapshot, id);
                    return;
                }
                // Adopt the most developed matching legacy Genius conversation without rewriting other records.
                let key;
                try { key = JSON.parse(cursor.key); } catch { key = null; }
                if (Array.isArray(key) && key.length === 4 && peers.has(key[2]) && key[3] === npc &&
                    cursor.value.revision > (snapshot?.revision ?? 0))
                    snapshot = cursor.value;
                cursor.continue();
            };
        };
    });
}

export async function save(id, revision, json) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(sessions, 'readwrite');
        const store = tx.objectStore(sessions);
        const read = store.get(id);
        let saved = false;
        read.onsuccess = () => {
            if ((read.result?.revision ?? 0) !== revision - 1) return;
            store.put({ revision, json }, id);
            saved = true;
        };
        tx.oncomplete = () => resolve(saved);
        tx.onabort = () => reject(tx.error || new Error('A gravação do diálogo foi interrompida.'));
    });
}

// Optional notes use the existing plugin scope and are independent of conversation turns.
function guardianMemoryScope() {
    const { api, room, id } = game.net.info();
    return JSON.stringify([api, room, id, 'guardia']);
}

export function handleMemoryCommand(command, text) {
    return memoryCommand(guardianMemoryScope(), command, text);
}

export async function generate(id, engine, json, maxTokens, toolsJson = null) {
    const { instructions, history, user } = JSON.parse(json);
    const model = engine === 'gpu' ? game.ai : game.aiCpu;
    const controller = new AbortController();
    attempts.set(id, controller);
    try {
        const message = await contextualize(guardianMemoryScope(), user);
        controller.signal.throwIfAborted();
        return await model.garden(instructions, message, maxTokens, history, controller.signal,
            toolsJson === null ? undefined : JSON.parse(toolsJson));
    } finally {
        attempts.delete(id);
    }
}

export function cancel(id) { attempts.get(id)?.abort(); }
