import assert from 'node:assert/strict';
import { appendFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { EXCEPTIONS_FILE, git, inspectFileSizes } from './repository-file-size.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));

function fixture(t) {
    const directory = mkdtempSync(join(tmpdir(), 'ultima-file-size-'));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    git(directory, 'init', '--quiet');

    const policy = join(directory, EXCEPTIONS_FILE);
    mkdirSync(dirname(policy));
    writeFileSync(policy, '{}');
    return { directory, policy, source: join(directory, 'new file.txt') };
}

test('repository files respect their line limits', () => {
    const violations = inspectFileSizes(root);
    assert.equal(violations.length, 0, violations.join('\n'));
});

test('400 lines pass and 401 fail with CRLF and an unterminated final line', t => {
    const { directory, source } = fixture(t);
    writeFileSync(source, '\r\n'.repeat(399) + 'last');
    assert.deepEqual(inspectFileSizes(directory), []);
    appendFileSync(source, '\r\nextra');
    assert.deepEqual(inspectFileSizes(directory), ['new file.txt: 401 lines exceed the limit of 400.']);
});

test('ignored and binary files are skipped while tracked files are checked', t => {
    const { directory, source } = fixture(t);
    writeFileSync(source, '\n'.repeat(401));
    git(directory, 'add', 'new file.txt');
    writeFileSync(join(directory, '.gitignore'), '*.txt\n');
    writeFileSync(join(directory, 'ignored.txt'), '\n'.repeat(500));
    writeFileSync(join(directory, 'binary'), '\0' + '\n'.repeat(500));
    assert.deepEqual(inspectFileSizes(directory), ['new file.txt: 401 lines exceed the limit of 400.']);
});

test('a justified exception has a ceiling and becomes stale when unused', t => {
    const { directory, policy, source } = fixture(t);
    writeFileSync(policy, JSON.stringify({ 'new file.txt': { maxLines: 401, reason: 'Fixture' } }));
    writeFileSync(source, '\n'.repeat(401));
    assert.deepEqual(inspectFileSizes(directory), []);
    appendFileSync(source, '\n');
    assert.deepEqual(inspectFileSizes(directory), ['new file.txt: 402 lines exceed the limit of 401.']);
    writeFileSync(source, '\n'.repeat(400));
    const stale = ['new file.txt: remove the unused file-size exception.'];
    assert.deepEqual(inspectFileSizes(directory), stale);
    rmSync(source);
    assert.deepEqual(inspectFileSizes(directory), stale);
});

test('an exception requires a reason', t => {
    const { directory, policy } = fixture(t);
    writeFileSync(policy, '{"new file.txt":{"maxLines":401,"reason":" "}}');
    assert.throws(() => inspectFileSizes(directory), /needs a maxLines integer above 400 and a reason/);
});
