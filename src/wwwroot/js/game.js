// Ultima — cena Babylon.js + teclado, Gamepad API e analógicos virtuais de toque.
(function () {
    'use strict';

    const WORLD_HALF = 24;
    const NPC_POSITION = { x: 0, z: -14 };
    const NEAR_RADIUS = 3.2;
    const SPEED = 6;
    const TICK_MS = 200;
    const DEADZONE = 0.18;

    let engine = null;
    let scene = null;
    let player = null;
    let camera = null;
    let dotnet = null;
    let near = false;
    let frozen = false;
    let lastTick = 0;
    let actionWasDown = false;
    let moveStick = { x: 0, y: 0 };
    let lookStick = { x: 0, y: 0 };
    let remoteSerial = 0;
    const remotes = new Map();
    const keys = Object.create(null);

    const gameCodes = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'];
    const editable = (el) => !!(el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName)));

    function interact() {
        if (dotnet) { dotnet.invokeMethodAsync('OnInteract'); }
    }

    function onKeyDown(e) {
        if (editable(e.target)) {
            if (e.code === 'Escape') { e.preventDefault(); if (dotnet) { dotnet.invokeMethodAsync('OnClose'); } }
            return;
        }
        if (gameCodes.includes(e.code)) {
            keys[e.code] = true;
            e.preventDefault();
        } else if (e.code === 'KeyE') {
            e.preventDefault();
            interact();
        } else if (e.code === 'Escape') {
            e.preventDefault();
            if (dotnet) { dotnet.invokeMethodAsync('OnClose'); }
        }
    }

    function onKeyUp(e) {
        if (gameCodes.includes(e.code)) {
            keys[e.code] = false;
            e.preventDefault();
        }
    }

    function setupVirtualStick(hostId, thumbId, get, set) {
        const host = document.getElementById(hostId);
        const thumb = document.getElementById(thumbId);
        if (!host || !thumb) { return; }
        let pointer = null;
        const reset = () => { pointer = null; set({ x: 0, y: 0 }); thumb.style.transform = 'translate(0, 0)'; };
        host.addEventListener('pointerdown', (e) => {
            pointer = e.pointerId;
            try { host.setPointerCapture(pointer); } catch (err) { /* captura opcional */ }
            e.preventDefault();
            update(e);
        });
        host.addEventListener('pointermove', (e) => { if (e.pointerId === pointer) { update(e); } });
        host.addEventListener('pointerup', (e) => { if (e.pointerId === pointer) { reset(); } });
        host.addEventListener('pointercancel', (e) => { if (e.pointerId === pointer) { reset(); } });
        function update(e) {
            const r = host.getBoundingClientRect();
            const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            const radius = Math.min(r.width, r.height) * 0.37;
            let dx = (e.clientX - cx) / radius, dy = (e.clientY - cy) / radius;
            const length = Math.hypot(dx, dy);
            if (length > 1) { dx /= length; dy /= length; }
            thumb.style.transform = `translate(${dx * radius}px, ${dy * radius}px)`;
            set({ x: dx, y: -dy });
        }
        void get;
    }

    function setupTouchControls() {
        setupVirtualStick('move-stick', 'move-thumb', () => moveStick, (v) => { moveStick = v; });
        setupVirtualStick('look-stick', 'look-thumb', () => lookStick, (v) => { lookStick = v; });
        const action = document.getElementById('touch-action');
        if (action) {
            action.addEventListener('pointerdown', (e) => { e.preventDefault(); interact(); });
        }
    }

    function clampToWorld(mesh) {
        mesh.position.x = Math.max(-WORLD_HALF, Math.min(WORLD_HALF, mesh.position.x));
        mesh.position.z = Math.max(-WORLD_HALF, Math.min(WORLD_HALF, mesh.position.z));
    }

    function buildScene(canvas) {
        scene = new BABYLON.Scene(engine);
        scene.clearColor = new BABYLON.Color4(0.05, 0.07, 0.12, 1);
        scene.fogMode = BABYLON.Scene.FOGMODE_EXP2;
        scene.fogColor = new BABYLON.Color3(0.05, 0.07, 0.12);
        scene.fogDensity = 0.022;

        const hemi = new BABYLON.HemisphericLight('hemi', new BABYLON.Vector3(0, 1, 0), scene);
        hemi.intensity = 0.55;
        const sun = new BABYLON.DirectionalLight('sun', new BABYLON.Vector3(-0.4, -1, 0.6), scene);
        sun.position = new BABYLON.Vector3(20, 32, -24);
        sun.intensity = 0.9;

        const ground = BABYLON.MeshBuilder.CreateGround('ground', { width: WORLD_HALF * 2 + 8, height: WORLD_HALF * 2 + 8 }, scene);
        const groundMat = new BABYLON.StandardMaterial('groundMat', scene);
        groundMat.diffuseColor = new BABYLON.Color3(0.16, 0.32, 0.18);
        groundMat.specularColor = new BABYLON.Color3(0.02, 0.02, 0.02);
        ground.material = groundMat;

        const path = BABYLON.MeshBuilder.CreateBox('path', { width: 2.2, depth: WORLD_HALF * 2 }, scene);
        path.position.y = 0.02;
        const pathMat = new BABYLON.StandardMaterial('pathMat', scene);
        pathMat.diffuseColor = new BABYLON.Color3(0.42, 0.34, 0.24);
        path.material = pathMat;

        player = BABYLON.MeshBuilder.CreateSphere('player', { diameter: 1.1, segments: 16 }, scene);
        player.position = new BABYLON.Vector3(0, 0.55, 0);
        const playerMat = new BABYLON.StandardMaterial('playerMat', scene);
        playerMat.diffuseColor = new BABYLON.Color3(0.22, 0.55, 0.95);
        player.material = playerMat;

        const npc = BABYLON.MeshBuilder.CreateCylinder('npc', { diameterTop: 0, diameterBottom: 1.4, height: 2.4, tessellation: 12 }, scene);
        npc.position = new BABYLON.Vector3(NPC_POSITION.x, 1.2, NPC_POSITION.z);
        const npcMat = new BABYLON.StandardMaterial('npcMat', scene);
        npcMat.diffuseColor = new BABYLON.Color3(0.85, 0.62, 0.16);
        npcMat.emissiveColor = new BABYLON.Color3(0.25, 0.16, 0.02);
        npc.material = npcMat;

        const marker = BABYLON.MeshBuilder.CreateTorus('npcMarker', { diameter: 2.4, thickness: 0.12, tessellation: 32 }, scene);
        marker.position = new BABYLON.Vector3(NPC_POSITION.x, 0.08, NPC_POSITION.z);
        const markerMat = new BABYLON.StandardMaterial('markerMat', scene);
        markerMat.emissiveColor = new BABYLON.Color3(0.95, 0.75, 0.2);
        marker.material = markerMat;
        scene.registerBeforeRender(() => { marker.rotation.y += 0.02; });

        for (let i = 0; i < 14; i++) {
            const angle = (i / 14) * Math.PI * 2;
            const radius = WORLD_HALF - 1.5;
            const tree = BABYLON.MeshBuilder.CreateCylinder('tree' + i, { diameterTop: 0, diameterBottom: 1.8, height: 3.2 + (i % 4), tessellation: 8 }, scene);
            tree.position.set(Math.cos(angle) * radius, 1.8, Math.sin(angle) * radius);
            const treeMat = new BABYLON.StandardMaterial('treeMat' + i, scene);
            treeMat.diffuseColor = new BABYLON.Color3(0.1, 0.35, 0.16);
            tree.material = treeMat;
        }

        camera = new BABYLON.ArcRotateCamera('camera', -Math.PI / 2, Math.PI / 3.2, 18, player.position.clone(), scene);
        camera.lowerRadiusLimit = 8;
        camera.upperRadiusLimit = 30;
        camera.upperBetaLimit = Math.PI / 2.15;
        camera.wheelDeltaPercentage = 0.01;
        camera.attachControl(canvas, true);
    }

    function applyDeadzone(v) {
        if (Math.abs(v) < DEADZONE) { return 0; }
        return Math.sign(v) * (Math.abs(v) - DEADZONE) / (1 - DEADZONE);
    }

    function pollGamepad() {
        let x = 0, y = 0, lookX = 0, lookY = 0, action = false;
        try {
            const pads = navigator.getGamepads ? navigator.getGamepads() : [];
            const pad = Array.from(pads || []).find((p) => p && p.connected);
            if (pad) {
                x = applyDeadzone(pad.axes[0] || 0);
                y = -applyDeadzone(pad.axes[1] || 0);
                lookX = applyDeadzone(pad.axes[2] || 0);
                lookY = applyDeadzone(pad.axes[3] || 0);
                action = !!(pad.buttons[0] && pad.buttons[0].pressed);
            }
        } catch (e) { /* Gamepad API indisponível */ }
        if (action && !actionWasDown) { interact(); }
        actionWasDown = action;
        return { x: x, y: y, lookX: lookX, lookY: lookY };
    }

    function movePlayer(deltaSeconds, pad) {
        if (frozen || !player) { return; }
        let ix = (keys.KeyD || keys.ArrowRight ? 1 : 0) - (keys.KeyA || keys.ArrowLeft ? 1 : 0);
        let iz = (keys.KeyW || keys.ArrowUp ? 1 : 0) - (keys.KeyS || keys.ArrowDown ? 1 : 0);
        ix += pad.x + moveStick.x;
        iz += pad.y + moveStick.y;
        if (ix === 0 && iz === 0) { return; }

        // Movimento relativo à câmera, com limites e velocidade normalizada.
        const forward = camera.getForwardRay().direction;
        const fx = forward.x, fz = forward.z;
        const flen = Math.hypot(fx, fz) || 1;
        const nx = fz / flen, nz = -fx / flen;
        let dx = (fx / flen) * iz + nx * ix;
        let dz = (fz / flen) * iz + nz * ix;
        const len = Math.hypot(dx, dz) || 1;
        const magnitude = Math.min(1, Math.hypot(ix, iz));
        player.position.x += (dx / len) * SPEED * magnitude * deltaSeconds;
        player.position.z += (dz / len) * SPEED * magnitude * deltaSeconds;
        clampToWorld(player);
        player.rotation.y = Math.atan2(dx, dz);
    }

    function checkNear() {
        const dist = Math.hypot(player.position.x - NPC_POSITION.x, player.position.z - NPC_POSITION.z);
        const nowNear = dist < NEAR_RADIUS;
        if (nowNear !== near) {
            near = nowNear;
            if (dotnet) { dotnet.invokeMethodAsync('OnNearChanged', nowNear); }
        }
    }

    function addRemote(id, name) {
        if (remotes.has(id) || !scene) { return; }
        const serial = remoteSerial++;
        const avatar = BABYLON.MeshBuilder.CreateSphere('remote-' + id, { diameter: 1.15, segments: 12 }, scene);
        avatar.position = new BABYLON.Vector3((serial % 5 - 2) * 1.5, 0.58, -2 - Math.floor(serial / 5) * 2);
        const mat = new BABYLON.StandardMaterial('remote-mat-' + id, scene);
        mat.diffuseColor = new BABYLON.Color3(0.76, 0.32, 0.92);
        mat.emissiveColor = new BABYLON.Color3(0.10, 0.025, 0.14);
        avatar.material = mat;

        const label = BABYLON.MeshBuilder.CreatePlane('remote-label-' + id, { width: 2.8, height: 0.55 }, scene);
        label.position = avatar.position.add(new BABYLON.Vector3(0, 1.0, 0));
        label.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;
        const texture = new BABYLON.DynamicTexture('remote-name-' + id, { width: 512, height: 100 }, scene, false);
        texture.hasAlpha = true;
        texture.drawText(String(name || 'Jogador').slice(0, 28), 12, 70, 'bold 42px sans-serif', '#ffffff', 'transparent', true);
        const labelMat = new BABYLON.StandardMaterial('remote-label-mat-' + id, scene);
        labelMat.diffuseTexture = texture;
        labelMat.emissiveColor = new BABYLON.Color3(1, 1, 1);
        labelMat.opacityTexture = texture;
        label.material = labelMat;
        remotes.set(id, { avatar: avatar, label: label, material: mat, texture: texture, labelMat: labelMat });
        if (dotnet) { dotnet.invokeMethodAsync('OnRemoteCount', remotes.size); }
    }

    function updateRemote(id, x, z, ry) {
        const p = remotes.get(id);
        if (!p) { return; }
        p.avatar.position.x = Math.max(-WORLD_HALF, Math.min(WORLD_HALF, x));
        p.avatar.position.z = Math.max(-WORLD_HALF, Math.min(WORLD_HALF, z));
        p.avatar.rotation.y = ry;
        p.label.position.x = p.avatar.position.x;
        p.label.position.z = p.avatar.position.z;
        p.label.position.y = 1.55;
    }

    function removeRemote(id) {
        const p = remotes.get(id);
        if (!p) { return; }
        p.avatar.dispose(); p.label.dispose(); p.material.dispose(); p.texture.dispose(); p.labelMat.dispose();
        remotes.delete(id);
        if (dotnet) { dotnet.invokeMethodAsync('OnRemoteCount', remotes.size); }
    }

    function tick(nowMs) {
        if (nowMs - lastTick < TICK_MS) { return; }
        lastTick = nowMs;
        if (dotnet && player) {
            dotnet.invokeMethodAsync('OnMoveTick', player.position.x, player.position.z, engine.getFps());
        }
        if (window.game && game.net && game.net.sendPos && player) {
            game.net.sendPos(player.position.x, player.position.z, player.rotation.y);
        }
    }

    window.game = {
        init(canvasId, dotnetRef) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) { throw new Error('canvas ' + canvasId + ' não encontrado'); }
            dotnet = dotnetRef;
            engine = new BABYLON.Engine(canvas, true, { adaptToDeviceRatio: true, stencil: false });
            buildScene(canvas);
            setupTouchControls();
            window.addEventListener('keydown', onKeyDown);
            window.addEventListener('keyup', onKeyUp);
            window.addEventListener('blur', () => {
                for (const k of Object.keys(keys)) { keys[k] = false; }
                moveStick = { x: 0, y: 0 }; lookStick = { x: 0, y: 0 };
            });

            engine.runRenderLoop(() => {
                const delta = Math.min(0.05, engine.getDeltaTime() / 1000);
                const pad = pollGamepad();
                if (!frozen) {
                    camera.alpha -= (pad.lookX + lookStick.x) * delta * 2.2;
                    camera.beta = Math.max(0.18, Math.min(Math.PI / 2.15, camera.beta + (pad.lookY + lookStick.y) * delta * 1.7));
                }
                movePlayer(delta, pad);
                checkNear();
                camera.setTarget(player.position);
                scene.render();
                tick(performance.now());
            });
            window.addEventListener('resize', () => engine.resize());

            scene.executeWhenReady(() => {
                let rendererName = 'WebGL';
                try {
                    const glInfo = engine.getGlInfo();
                    if (glInfo && (glInfo.renderer || glInfo.vendor)) { rendererName = glInfo.renderer || glInfo.vendor; }
                } catch (e) { /* identificação não suportada */ }
                if (dotnet) { dotnet.invokeMethodAsync('OnEngineReady', String(rendererName)); }
            });
            canvas.focus();
            return true;
        },
        setFrozen(value) {
            frozen = !!value;
            if (frozen) { for (const k of Object.keys(keys)) { keys[k] = false; } }
        },
        resetPlayer() {
            if (player) { player.position.x = 0; player.position.z = 0; player.rotation.y = 0; }
            if (camera) { camera.alpha = -Math.PI / 2; camera.beta = Math.PI / 3.2; }
        },
        addRemote,
        updateRemote,
        removeRemote
    };
})();
