// Chromium logs an HTTP error even when the application handles the response.
module.exports = function observeCommandConflicts(page) {
    const observations = [];
    page.on('response', response => {
        if (response.status() !== 409 || new URL(response.url()).pathname !== '/v1/game/command') return;
        observations.push((async () => {
            let expected = false;
            try {
                const request = response.request();
                const action = request.postDataJSON()?.action;
                const body = await response.json();
                expected = request.method() === 'POST' && action === 'pose' && body?.error === 'too_fast';
            } catch { /* Missing or unreadable evidence counts as an unexpected conflict. */ }
            return { url: response.url(), expected };
        })());
    });

    return async function classify(errors) {
        const seen = await Promise.all(observations);
        const remaining = new Map();
        const failures = [];
        for (const { url, expected } of seen) {
            if (expected) remaining.set(url, (remaining.get(url) || 0) + 1);
            else failures.push('HTTP 409 inesperado em /v1/game/command');
        }
        let tolerated = 0;
        for (const error of errors) {
            const match = /^Failed to load resource: the server responded with a status of 409 \(Conflict\) <<([^>]+)>>$/.exec(error);
            if (match && remaining.get(match[1]) > 0) {
                remaining.set(match[1], remaining.get(match[1]) - 1);
                tolerated++;
            } else if (!/favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen/i.test(error)) {
                failures.push(error);
            }
        }
        return { failures, tolerated };
    };
};
