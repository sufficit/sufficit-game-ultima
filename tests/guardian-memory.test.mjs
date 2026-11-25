import test from 'node:test';
import assert from 'node:assert/strict';
import { createLocalStorageMemory } from '../src/wwwroot/js/local-storage-memory.mjs';
import { useMemoryPlugin, handleMemoryCommand, generate } from '../src/wwwroot/js/guardian-genius.mjs';

function storage() {
    const values = new Map();
    return {
        get length() { return values.size; },
        key: i => [...values.keys()][i],
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value)
    };
}

test('reabre dados, deduplica e isola por namespace e escopo', async () => {
    const disk = storage();
    const open = () => createLocalStorageMemory(() => disk);
    const provider = open();
    const writes = await Promise.all(['A maçã azul', 'A maçã azul', 'A pera'].map(text => provider.save('jogo', text)));
    assert.equal(writes[0].id, writes[1].id);
    assert.equal(disk.length, 2);
    disk.setItem(disk.key(0) + 'invalido', '{');
    assert.deepEqual((await open().search('jogo', 'MACA')).map(x => x.text), ['A maçã azul']);
    assert.equal((await open().search('jogo', '', 1)).length, 1);
    assert.deepEqual(await open().search('jogo:2'), []);
    assert.deepEqual(await createLocalStorageMemory(() => disk, 'outro').search('jogo'), []);
});

test('troca o plugin durante a execução e passa as notas apenas na mensagem do modelo local', async () => {
    globalThis.game = { net: { info: () => ({ room: 'r', id: 'p', api: 'a' }) },
        aiCpu: { garden: (...args) => args } };
    const disk = storage();
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: disk });
    const request = JSON.stringify({ instructions: 'regra', history: [], user: 'azul' });
    try {
        await handleMemoryCommand('/lembrar', 'azul padrão');
        useMemoryPlugin(createLocalStorageMemory(storageOnce()));
        assert.equal(await handleMemoryCommand('/memorias', ''), 'Nenhuma memória encontrada.');
        assert.equal(await handleMemoryCommand('/lembrar', 'azul alternativo'), 'Memória salva.');
        const [instructions, message, , history] = await generate('turno', 'cpu', request, 80);
        assert.equal(instructions, 'regra');
        assert.deepEqual(history, []);
        assert.match(message, /azul alternativo/);
        assert.doesNotMatch(message, /azul padrão/);
        useMemoryPlugin({ save: async () => ({}), search: () => { throw Error('indisponível'); } });
        assert.match(await handleMemoryCommand('/lembrar', 'azul'), /sem confirmação/);
        assert.equal((await generate('turno', 'cpu', request, 80))[1], 'azul');
        useMemoryPlugin(createLocalStorageMemory(() => { throw Error('acesso bloqueado'); }));
        assert.match(await handleMemoryCommand('/lembrar', 'azul'), /Não consegui/);
        assert.throws(() => useMemoryPlugin({}), TypeError);
        useMemoryPlugin();
        assert.equal(await handleMemoryCommand('/memorias', ''), 'azul padrão');
    } finally {
        useMemoryPlugin();
        delete globalThis.game;
        delete globalThis.localStorage;
    }
});

function storageOnce() {
    const disk = storage();
    return () => disk;
}
