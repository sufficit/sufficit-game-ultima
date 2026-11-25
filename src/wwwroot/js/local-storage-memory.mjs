// Each memory has its own key. Tabs adding different records cannot overwrite each other.
const normalize = text => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
const words = text => new Set(normalize(text).match(/[\p{L}\p{N}]+/gu) || []);

export function createLocalStorageMemory(getStorage = () => globalThis.localStorage, namespace = 'memory.v1') {
    const prefix = scope => encodeURIComponent(namespace) + ':' + encodeURIComponent(scope) + ':';
    return {
        async save(scope, text) {
            const value = text.trim();
            if (!value || value.length > 300) throw new RangeError('Memória vazia ou muito longa.');
            const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
            const id = Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
            getStorage().setItem(prefix(scope) + id, JSON.stringify({ text: value, at: Date.now() }));
            return { id };
        },

        async search(scope, query = '', limit = 5) {
            const storage = getStorage();
            const start = prefix(scope);
            const terms = words(query);
            const matches = [];
            for (let i = 0; i < storage.length; i++) {
                const key = storage.key(i);
                if (!key?.startsWith(start)) continue;
                let record;
                try { record = JSON.parse(storage.getItem(key)); }
                catch (error) { if (error instanceof SyntaxError) continue; throw error; }
                if (typeof record?.text !== 'string' || !Number.isFinite(record.at)) continue;
                const tokens = words(record.text);
                const score = [...terms].filter(term => tokens.has(term)).length;
                if (!terms.size || score) matches.push({ id: key.slice(start.length), text: record.text, at: record.at, score });
            }
            return matches.sort((a, b) => b.score - a.score || b.at - a.at || a.id.localeCompare(b.id)).slice(0, limit);
        }
    };
}
