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

export function guardianConversationId() {
    const { api, room, id } = game.net.info();
    return JSON.stringify([api, room, id, 'guardia']);
}

export async function load(id) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const request = db.transaction(sessions).objectStore(sessions).get(id);
        request.onsuccess = () => resolve(request.result?.json ?? null);
        request.onerror = () => reject(request.error);
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

export function handleMemoryCommand(command, text) {
    return memoryCommand(guardianConversationId(), command, text);
}

export async function generate(id, engine, json, maxTokens, scope = guardianConversationId()) {
    const { instructions, history, user } = JSON.parse(json);
    const model = engine === 'gpu' ? game.ai : game.aiCpu;
    const controller = new AbortController();
    attempts.set(id, controller);
    try {
        const message = await contextualize(scope, user);
        controller.signal.throwIfAborted();
        return await model.garden(instructions, message, maxTokens, history, controller.signal);
    } finally {
        attempts.delete(id);
    }
}

export function cancel(id) { attempts.get(id)?.abort(); }
