export function attachWalk(B, canvas, scene, spawn, eyeHeight, exports) {
    const camera = new B.UniversalCamera('environment-camera', new B.Vector3(spawn.x, eyeHeight, spawn.z), scene);
    camera.rotation.y = spawn.yaw;
    camera.minZ = .04;
    camera.fov = .98;

    let dx = 0, dy = 0, aim, joystick;
    const keys = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ShiftLeft', 'ShiftRight'];
    const listeners = new AbortController();
    const on = (target, type, handler) => target.addEventListener(type, handler, { signal: listeners.signal });
    on(canvas, 'keydown', event => {
        if (!keys.includes(event.code) || event.ctrlKey || event.metaKey || event.altKey) return;
        event.preventDefault();
        if (!event.repeat) exports.Key(event.code, true);
    });
    on(canvas, 'keyup', event => {
        if (keys.includes(event.code)) exports.Key(event.code, false);
    });

    function clear() {
        exports.ClearInput();
        aim = joystick = undefined;
        dx = dy = 0;
    }

    on(canvas, 'blur', clear);
    on(window, 'blur', clear);
    on(document, 'visibilitychange', () => { if (document.hidden) clear(); });
    on(canvas, 'pointerdown', event => {
        if (event.button !== 0) return;
        canvas.focus({ preventScroll: true });
        const rect = canvas.getBoundingClientRect();
        const point = { id: event.pointerId, x: event.clientX, y: event.clientY, side: 0, forward: 0 };
        if (event.pointerType === 'touch' && event.clientX < rect.left + rect.width * .46) {
            if (joystick) return;
            joystick = point;
        } else {
            if (aim) return;
            aim = point;
        }
        canvas.setPointerCapture(event.pointerId);
    });

    on(canvas, 'pointermove', event => {
        if (aim?.id === event.pointerId) {
            dx += event.clientX - aim.x;
            dy += event.clientY - aim.y;
            aim.x = event.clientX;
            aim.y = event.clientY;
        }
        if (joystick?.id === event.pointerId) {
            joystick.side = Math.max(-1, Math.min(1, (event.clientX - joystick.x) / 44));
            joystick.forward = Math.max(-1, Math.min(1, (joystick.y - event.clientY) / 44));
        }
    });
    for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) {
        on(canvas, type, event => {
            if (aim?.id === event.pointerId) aim = undefined;
            if (joystick?.id === event.pointerId) joystick = undefined;
        });
    }

    return {
        step(dt) {
            if (document.activeElement !== canvas || document.hidden) return;
            let pad;
            try { pad = navigator.getGamepads?.().find(candidate => candidate?.connected); }
            catch { /* Gamepad access can be blocked by browser policy. */ }
            const axes = pad?.axes;
            const [x, z, yaw, pitch] = exports.WalkFrame(joystick?.side ?? (axes?.[0] || 0),
                joystick?.forward ?? -(axes?.[1] || 0), axes?.[2] || 0, axes?.[3] || 0, dx, dy, dt);
            dx = dy = 0;
            camera.position.set(x, eyeHeight, z);
            camera.rotation.set(pitch, yaw, 0);
        },
        dispose() {
            listeners.abort();
            clear();
        }
    };
}
