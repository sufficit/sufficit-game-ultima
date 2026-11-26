export function attachMouseLook(canvas, look, clear, signal) {
    let active = false, paused = false, locked = false, lockStarted = -Infinity;
    let frame = 0, x = 0, y = 0;
    const on = (target, type, handler) => target.addEventListener(type, handler, { signal });

    function release() {
        paused = true;
        resetMotion();
        if (document.pointerLockElement === canvas) document.exitPointerLock();
    }

    function resetMotion() {
        cancelAnimationFrame(frame);
        frame = x = y = 0;
    }

    function pause() {
        if (!active || paused) return;
        release();
        clear();
    }

    function start(capture = true) {
        paused = false;
        canvas.focus({ preventScroll: true });
        if (capture && navigator.userActivation?.isActive && matchMedia('(any-pointer: fine)').matches) {
            try { canvas.requestPointerLock?.()?.catch(() => {}); }
            catch { /* Continue with mouse look over the canvas. */ }
        }
    }

    on(document, 'pointerlockchange', () => {
        const captured = document.pointerLockElement === canvas;
        if (captured && (!active || paused)) document.exitPointerLock();
        if (!captured && locked && active && !paused) pause();
        if (captured && !locked) lockStarted = performance.now();
        locked = captured;
    });
    on(canvas, 'blur', pause);
    on(window, 'blur', pause);
    on(document, 'visibilitychange', () => { if (document.hidden) pause(); });
    on(canvas, 'pointerdown', event => {
        if (active && paused && event.pointerType === 'mouse' && event.button === 0) {
            event.preventDefault();
            start();
        }
    });
    on(canvas, 'mousemove', event => {
        if (!active || paused || document.activeElement !== canvas || document.hidden) return;
        // The pointer can warp before the browser dispatches pointerlockchange.
        if (Math.max(Math.abs(event.movementX), Math.abs(event.movementY)) > 125 &&
            document.pointerLockElement === canvas && (!locked || performance.now() - lockStarted < 150)) return;
        x += event.movementX;
        y += event.movementY;
        if ((!x && !y) || frame) return;
        frame = requestAnimationFrame(() => {
            look(x, y);
            frame = x = y = 0;
        });
    });
    signal?.addEventListener('abort', pause, { once: true });

    return {
        setActive(value, capture) {
            if (active === value) return;
            if (!value) release();
            active = value;
            if (value) start(capture);
        },
        pause,
        get active() { return active && !paused; }
    };
}
