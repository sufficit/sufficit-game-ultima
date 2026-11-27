import assert from 'node:assert/strict';
import test from 'node:test';
import '../src/wwwroot/js/graphics-budget.js';

const { create, activeFrameMs, idleFrameMs } = globalThis.UltimaGraphicsBudget;

test('Economy targets 30 active and 15 idle draws per second', () => {
    assert.equal(activeFrameMs, 1000 / 30);
    assert.equal(idleFrameMs, 1000 / 15);
});

test('The resolution drops under sustained pressure and recovers gradually', () => {
    const budget = create();
    let now = 0;
    const frames = (duration, count, submit = 4) => {
        for (let i = 0; i < count; i++) {
            now += duration;
            budget.sample(now, duration, submit);
        }
    };

    frames(60, 25);
    assert.equal(budget.scale, 1);
    frames(60, 5);
    assert.equal(budget.scale, .85);
    frames(60, 40);
    assert.equal(budget.scale, .70);
    frames(34, 190);
    assert.equal(budget.scale, .70);
    frames(34, 85);
    assert.equal(budget.scale, .85);
    frames(34, 250);
    assert.equal(budget.scale, 1);
});

test('Reset drops stale timings while keeping the selected scale', () => {
    const budget = create();
    for (let i = 0; i < 30; i++) budget.sample(i * 60, 60, 6);
    assert.equal(budget.scale, .85);
    budget.reset();
    budget.sample(10000, 1000, 28);
    assert.equal(budget.scale, .85);
});
