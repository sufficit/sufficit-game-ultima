import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

const script = readFileSync(new URL('../src/wwwroot/js/player-identity.js', import.meta.url), 'utf8');
const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

function openTab(storage) {
    const window = { game: {} };
    runInNewContext(script, { window, localStorage: storage, crypto: webcrypto });
    return window.game.player.uuid;
}

function browserProfile(values = new Map()) {
    return {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value),
    };
}

test('tabs share the UUID and different browser profiles have independent identities', () => {
    const profile = browserProfile();
    const first = openTab(profile)();
    assert.match(first, uuidV4);
    assert.equal(openTab(profile)(), first);
    assert.notEqual(openTab(browserProfile())(), first);
});

test('invalid stored values are replaced and restricted storage keeps one page UUID', () => {
    const upgraded = browserProfile(new Map([['ultima-player-uuid', 'invalid']]));
    const first = openTab(upgraded)();
    assert.match(first, uuidV4);
    assert.equal(upgraded.getItem('ultima-player-uuid'), first);

    const denied = () => { throw new Error('Storage blocked'); };
    const getUuid = openTab({ getItem: denied, setItem: denied });
    const temporary = getUuid();
    assert.match(temporary, uuidV4);
    assert.equal(getUuid(), temporary);
});
