(function (root) {
    'use strict';

    const scales = [1, .85, .70];
    const activeFrameMs = 1000 / 30;
    const idleFrameMs = 1000 / 15;

    function create() {
        let level = 0, startedAt = null, samples = 0, late = 0, stableWindows = 0;

        function reset() {
            startedAt = null;
            samples = late = stableWindows = 0;
        }

        function sample(now, interval, submitMs) {
            startedAt ??= now;
            samples++;
            // Late frames cover GPU backpressure as well as slow rendering on the browser thread.
            if (interval > activeFrameMs * 1.35 || submitMs > activeFrameMs * .8) late++;
            if (samples < 12 || now - startedAt < 1600) return false;

            const previous = level;
            const lateShare = late / samples;
            if (lateShare >= .35) {
                level = Math.min(scales.length - 1, level + 1);
                stableWindows = 0;
            } else if (lateShare <= .08) {
                if (++stableWindows >= 5) {
                    level = Math.max(0, level - 1);
                    stableWindows = 0;
                }
            } else {
                stableWindows = 0;
            }

            startedAt = now;
            samples = late = 0;
            return previous !== level;
        }

        return { get scale() { return scales[level]; }, reset, sample };
    }

    root.UltimaGraphicsBudget = { create, activeFrameMs, idleFrameMs };
})(globalThis);
