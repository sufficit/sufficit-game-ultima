import { execFileSync } from 'node:child_process';
import { lstatSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const LINE_LIMIT = 400;
export const EXCEPTIONS_FILE = 'scripts/file-size-exceptions.json';

export function git(directory, ...args) {
    return execFileSync('git', args, { cwd: directory, encoding: 'utf8' });
}

function countTextLines(content) {
    // Match the C# suite's NUL-byte heuristic and count an unterminated final line.
    if (content.includes(0)) return 0;
    let lines = 0;
    for (const byte of content) {
        if (byte === 10) lines++;
    }
    return lines + Number(content.length > 0 && content.at(-1) !== 10);
}

export function inspectFileSizes(root) {
    const exceptions = JSON.parse(readFileSync(join(root, EXCEPTIONS_FILE), 'utf8'));
    if (!exceptions || typeof exceptions !== 'object' || Array.isArray(exceptions)) {
        throw new Error('Expected file-size exceptions keyed by repository-relative path.');
    }

    for (const [name, entry] of Object.entries(exceptions)) {
        if (!entry || !Number.isInteger(entry.maxLines) || entry.maxLines <= LINE_LIMIT ||
            entry.maxLines > 2_147_483_647 || typeof entry.reason !== 'string' || !entry.reason.trim()) {
            throw new Error(`${name} needs a maxLines integer above ${LINE_LIMIT} and a reason.`);
        }
    }

    const files = git(root, 'ls-files', '-z', '--cached', '--others', '--exclude-standard');
    const failures = [];
    const active = new Set();
    for (const name of [...new Set(files.split('\0').filter(Boolean))].sort()) {
        const path = join(root, name);
        // lstat checks the path itself so a symlink cannot bring in files outside the repository.
        if (!lstatSync(path, { throwIfNoEntry: false })?.isFile()) continue;
        const lines = countTextLines(readFileSync(path));
        if (lines <= LINE_LIMIT) continue;

        active.add(name);
        const ceiling = Object.hasOwn(exceptions, name) ? exceptions[name].maxLines : LINE_LIMIT;
        if (lines > ceiling) failures.push(`${name}: ${lines} lines exceed the limit of ${ceiling}.`);
    }

    for (const name of Object.keys(exceptions).sort()) {
        if (!active.has(name)) failures.push(`${name}: remove the unused file-size exception.`);
    }
    return failures;
}
