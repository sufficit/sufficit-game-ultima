export function attachWalk(B, canvas, scene, avatar, exports, attachMouseLook) {
    const camera = new B.UniversalCamera('environment-camera', B.Vector3.Zero(), scene);
    camera.minZ = .04;
    camera.fov = .98;

    let dx = 0, dy = 0, wheel = 0, aim, joystick, viewWasDown = false;
    const keys = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ShiftLeft', 'ShiftRight'];
    const listeners = new AbortController();
    const on = (target, type, handler) => target.addEventListener(type, handler, { signal: listeners.signal });
    const mouseLook = attachMouseLook(canvas, (x, y) => { dx += x; dy += y; }, clear, listeners.signal);
    on(canvas, 'keydown', event => {
        if (event.code === 'Escape' && mouseLook.active) {
            event.preventDefault();
            mouseLook.pause();
            return;
        }
        if (event.ctrlKey || event.metaKey || event.altKey) return;
        if (event.code === 'KeyV') {
            event.preventDefault();
            if (!event.repeat) cycle();
            return;
        }
        if (!keys.includes(event.code)) return;
        event.preventDefault();
        if (!event.repeat) exports.Key(event.code, true);
    });
    on(canvas, 'keyup', event => {
        if (keys.includes(event.code)) exports.Key(event.code, false);
    });

    function clear() {
        exports.ClearInput();
        aim = joystick = undefined;
        dx = dy = wheel = 0;
    }

    on(canvas, 'blur', clear);
    on(window, 'blur', clear);
    on(document, 'visibilitychange', () => { if (document.hidden) clear(); });
    on(canvas, 'mousedown', event => { if (event.button === 1) event.preventDefault(); });
    on(canvas, 'auxclick', event => { if (event.button === 1) event.preventDefault(); });
    canvas.addEventListener('wheel', event => {
        if (activeView !== 2) return;
        event.preventDefault();
        wheel += event.deltaY * (event.deltaMode === 1 ? 40 : 1);
    }, { passive: false, signal: listeners.signal });
    on(canvas, 'pointerdown', event => {
        if (event.button === 1) {
            event.preventDefault();
            canvas.focus({ preventScroll: true });
            cycle();
            return;
        }
        if (event.button !== 0 || (event.pointerType === 'mouse' && mouseLook.active)) return;
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

    const target = new B.Vector3();
    let activeView = -1, avatarVisible = true;
    function applyPose(pose, capture = true) {
        const cameraX = pose[3], cameraY = pose[4], cameraZ = pose[5];
        const targetX = pose[6], targetY = pose[7], targetZ = pose[8], view = pose[9];
        avatar.apply(pose);
        camera.position.set(cameraX, cameraY, cameraZ);
        target.set(targetX, targetY, targetZ);
        if (activeView !== view) {
            activeView = view;
            mouseLook.setActive(view === 2, capture);
            camera.metadata = view;
        }
        const visible = view !== 1 && (view !== 2 || B.Vector3.DistanceSquared(camera.position, target) >= .38 ** 2);
        if (visible !== avatarVisible) {
            avatar.setVisible(visible);
            avatarVisible = visible;
        }
        camera.setTarget(target);
    }
    applyPose(exports.WalkFrame(0, 0, 0, 0, 0, 0, 0, 0));

    function cycle(capture = true) {
        wheel = 0;
        exports.CycleCameraView();
        applyPose(exports.WalkFrame(0, 0, 0, 0, 0, 0, 0, 0), capture);
    }

    canvas.focus({ preventScroll: true });
    let finishingStep = false;
    return {
        cycle,
        step(dt) {
            if (document.hidden) return;
            let pad;
            try { pad = navigator.getGamepads?.().find(candidate => candidate?.connected); }
            catch { /* Gamepad access can be blocked by browser policy. */ }
            const view = !!pad?.buttons[11]?.pressed;
            const cyclePressed = view && !viewWasDown;
            viewWasDown = view;
            if (document.activeElement !== canvas) {
                if (finishingStep) {
                    applyPose(exports.WalkFrame(0, 0, 0, 0, 0, 0, 0, 0));
                    finishingStep = false;
                }
                return;
            }
            finishingStep = true;
            if (cyclePressed) cycle(false);
            const axes = pad?.axes;
            applyPose(exports.WalkFrame(joystick?.side ?? (axes?.[0] || 0),
                joystick?.forward ?? -(axes?.[1] || 0), axes?.[2] || 0, axes?.[3] || 0, dx, dy, wheel, dt));
            dx = dy = wheel = 0;
        },
        dispose() {
            listeners.abort();
            clear();
        }
    };
}
