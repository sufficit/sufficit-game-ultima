const { test } = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const observe = require('./game-command-conflicts.cjs');

const url = 'http://localhost/v1/game/command';
const message = 'Failed to load resource: the server responded with a status of 409 (Conflict) <<' + url + '>>';

function conflict(page, method, action, error) {
    page.emit('response', {
        status: () => 409, url: () => url,
        request: () => ({ method: () => method, postDataJSON: () => ({ action }) }),
        json: async () => ({ error }),
    });
}

test('cada pose/too_fast comprovado tolera uma ocorrência no console', async () => {
    const page = new EventEmitter();
    const classify = observe(page);
    conflict(page, 'POST', 'pose', 'too_fast');
    assert.deepEqual(await classify([message, message]), { failures: [message], tolerated: 1 });
});

for (const args of [['GET', 'pose', 'too_fast'], ['POST', 'order', 'too_fast'], ['POST', 'pose', 'busy']]) {
    test('conflito inesperado falha mesmo sem mensagem no console: ' + args.join('/'), async () => {
        const page = new EventEmitter();
        const classify = observe(page);
        conflict(page, ...args);
        const { failures, tolerated } = await classify([]);
        assert.equal(failures.length, 1);
        assert.equal(tolerated, 0);
    });
}
