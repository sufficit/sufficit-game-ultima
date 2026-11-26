import assert from 'node:assert/strict';
import test from 'node:test';
import { createRoomPresence } from '../src/wwwroot/js/room-presence.mjs';

function fixture(environment) {
    const calls = [];
    const presence = createRoomPresence(environment, {
        addRemote: (...args) => calls.push(['add', ...args]),
        updateRemote: (...args) => calls.push(['update', ...args]),
        removeRemote: (...args) => calls.push(['remove', ...args])
    });
    return { calls, presence };
}

const position = { x: 2, z: 3, ry: 1 };

test('a pose identifies its loaded environment', () => {
    const { presence } = fixture('office');
    assert.deepEqual(JSON.parse(presence.pose(2, 3, 1)), { t: 'pos', environment: 'office', ...position });
});

test('a player appears after a local pose and disappears on an environment change', () => {
    const { calls, presence } = fixture('office');
    presence.update('peer', 'Alex', { ...position, environment: 'garden' });
    assert.deepEqual(calls, []);
    presence.update('peer', 'Alex', { ...position, environment: 'office' });
    presence.update('peer', 'Alex', { ...position, x: 4, environment: 'office' });
    presence.update('peer', 'Alex', { ...position, environment: 'garden' });
    presence.update('peer', 'Alex', { ...position, environment: 'garden' });
    assert.deepEqual(calls, [
        ['add', 'peer', 'Alex'], ['update', 'peer', 2, 3, 1], ['update', 'peer', 4, 3, 1], ['remove', 'peer']
    ]);
    presence.update('peer', 'Alex', { ...position, environment: 'office' });
    assert.deepEqual(calls.slice(-2), [['add', 'peer', 'Alex'], ['update', 'peer', 2, 3, 1]]);
});

test('unscoped poses remain compatible with the Garden only', () => {
    const office = fixture('office');
    const garden = fixture('garden');
    office.presence.update('peer', 'Alex', position);
    garden.presence.update('peer', 'Alex', position);
    assert.deepEqual(office.calls, []);
    assert.deepEqual(garden.calls, [['add', 'peer', 'Alex'], ['update', 'peer', 2, 3, 1]]);
});

test('invalid poses create no avatar and disconnect clears visible presence', () => {
    const { calls, presence } = fixture('office');
    presence.update('peer', 'Alex', { ...position, x: Infinity, environment: 'office' });
    assert.deepEqual(calls, []);
    presence.update('peer', 'Alex', { ...position, environment: 'office' });
    presence.remove('peer');
    presence.remove('peer');
    assert.deepEqual(calls, [['add', 'peer', 'Alex'], ['update', 'peer', 2, 3, 1], ['remove', 'peer']]);
});
