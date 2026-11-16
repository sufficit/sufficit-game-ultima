// Ultima — cena Babylon.js com terreno procedural realista, teclado, Gamepad API e analógicos virtuais.
// Terreno: ruído Perlin 2D próprio (sem bibliotecas externas), coloração por vértice
// (grama/terra/rocha), trilha sinuosa aplainada, bordas montanhosas, céu com gradiente.
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

    // ---------- Ruído de Perlin 2D (valor/gradiente) ----------
    const lerp = (a, b, t) => a + (b - a) * t;
    const smoothstep = (a, b, t) => { const x = Math.max(0, Math.min(1, (t - a) / (b - a))); return x * x * (3 - 2 * x); };

    function makeNoise(seed) {
        let s = seed >>> 0;
        const rand = () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
        const p = Array.from({ length: 256 }, (_, i) => i);
        for (let i = 255; i > 0; i--) {
            const j = Math.floor(rand() * (i + 1));
            const tmp = p[i]; p[i] = p[j]; p[j] = tmp;
        }
        const perm = new Uint8Array(512);
        for (let i = 0; i < 512; i++) { perm[i] = p[i & 255]; }
        const fade = (t) => t * t * (3 - 2 * t);
        const grad = (h, x, y) => ((h & 1) ? x : -x) + ((h & 2) ? y : -y);
        return function noise2(x, y) {
            const xi = Math.floor(x), yi = Math.floor(y);
            const X = xi & 255, Y = yi & 255;
            const xf = x - xi, yf = y - yi;
            const u = fade(xf), v = fade(yf);
            const aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1];
            const ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
            const x1 = lerp(grad(aa, xf, yf), grad(ba, xf - 1, yf), u);
            const x2 = lerp(grad(ab, xf, yf - 1), grad(bb, xf - 1, yf - 1), u);
            return lerp(x1, x2, v);
        };
    }

    const noise = makeNoise(20261115);

    // Distância lateral até o eixo da trilha (trilha sinuosa, quase reta).
    function pathOffset(z) { return noise(z * 0.09 + 3.1, 17.7) * 1.6; }
    function pathBlend(x, z) { return smoothstep(1.5, 3.4, Math.abs(x - pathOffset(z))); }

    function terrainHeight(x, z) {
        let h = noise(x * 0.045, z * 0.045) * 2.6;
        h += noise(x * 0.11 + 40.2, z * 0.11 - 17.4) * 0.9;
        h += noise(x * 0.28 - 9.3, z * 0.28 + 23.8) * 0.22;
        const rim = smoothstep(WORLD_HALF - 9, WORLD_HALF - 2, Math.hypot(x, z));
        h += rim * (2.6 + noise(x * 0.2 + 5, z * 0.2 - 5) * 1.3);
        const flat = 1 - pathBlend(x, z);
        h = lerp(h, 0.06, flat * 0.92);
        const spawn = smoothstep(2.5, 6, Math.hypot(x, z));
        h = lerp(0.05, h, spawn);
        return h;
    }

    function slopeAt(x, z) {
        const e = 0.7;
        const dhx = Math.abs(terrainHeight(x + e, z) - terrainHeight(x - e, z));
        const dhz = Math.abs(terrainHeight(x, z + e) - terrainHeight(x, z - e));
        return Math.max(dhx, dhz) / (2 * e);
    }

    // ---------- Cena ----------
    function skyDome() {
        const sky = BABYLON.MeshBuilder.CreateSphere('sky', { diameter: 180, segments: 12, sideOrientation: BABYLON.Mesh.BACKSIDE }, scene);
        sky.infiniteDistance = true;
        sky.isPickable = false;
        const texture = new BABYLON.DynamicTexture('skyTex', { width: 16, height: 256 }, scene, false);
        const ctx = texture.getContext();
        const grad = ctx.createLinearGradient(0, 0, 0, 256);
        grad.addColorStop(0, '#2b4f8e');
        grad.addColorStop(0.5, '#7fa3cf');
        grad.addColorStop(0.72, '#c3d3e3');
        grad.addColorStop(1, '#e3e9ee');
        ctx.fillStyle = grad;
        ctx.fillRect(0, 0, 16, 256);
        texture.update(false);
        const mat = new BABYLON.StandardMaterial('skyMat', scene);
        mat.disableLighting = true;
        mat.emissiveTexture = texture;
        mat.diffuseColor = new BABYLON.Color3(0, 0, 0);
        mat.specularColor = new BABYLON.Color3(0, 0, 0);
        mat.fogEnabled = false;
        sky.material = mat;
    }

    function buildTerrain() {
        const size = WORLD_HALF * 2 + 8;
        const ground = BABYLON.MeshBuilder.CreateGround('ground', { width: size, height: size, subdivisions: 64 }, scene);
        const positions = ground.getVerticesData(BABYLON.VertexBuffer.PositionKind);
        const colors = new Float32Array((positions.length / 3) * 4);

        const grassDark = [0.13, 0.29, 0.12];
        const grassLight = [0.32, 0.46, 0.17];
        const dirt = [0.50, 0.39, 0.25];
        const rock = [0.45, 0.43, 0.40];

        for (let i = 0; i < positions.length; i += 3) {
            const x = positions[i], z = positions[i + 2];
            const y = terrainHeight(x, z);
            positions[i + 1] = y;

            const variation = noise(x * 0.15 + 100, z * 0.15 - 60) * 0.5 + 0.5;
            let r = lerp(grassDark[0], grassLight[0], Math.max(0, Math.min(1, y * 0.14 + 0.4 + variation * 0.3)));
            let g = lerp(grassDark[1], grassLight[1], Math.max(0, Math.min(1, y * 0.14 + 0.4 + variation * 0.3)));
            let b = lerp(grassDark[2], grassLight[2], Math.max(0, Math.min(1, y * 0.14 + 0.4 + variation * 0.3)));

            const path = pathBlend(x, z);
            r = lerp(dirt[0], r, path); g = lerp(dirt[1], g, path); b = lerp(dirt[2], b, path);

            const slope = slopeAt(x, z);
            const rocky = Math.max(0, Math.min(1, (slope - 0.45) / 0.5));
            r = lerp(r, rock[0], rocky); g = lerp(g, rock[1], rocky); b = lerp(b, rock[2], rocky);

            const ci = (i / 3) * 4;
            colors[ci] = r; colors[ci + 1] = g; colors[ci + 2] = b; colors[ci + 3] = 1;
        }
        ground.setVerticesData(BABYLON.VertexBuffer.PositionKind, positions);
        ground.setVerticesData(BABYLON.VertexBuffer.ColorKind, colors);
        const normals = [];
        BABYLON.VertexData.ComputeNormals(positions, ground.getIndices(), normals);
        ground.setVerticesData(BABYLON.VertexBuffer.NormalKind, normals);

        const groundMat = new BABYLON.StandardMaterial('groundMat', scene);
        groundMat.diffuseColor = new BABYLON.Color3(1, 1, 1);
        groundMat.specularColor = new BABYLON.Color3(0.02, 0.02, 0.02);
        ground.material = groundMat;
        return ground;
    }

    function scatterOnTerrain(proto, count, tries, minScale, maxScale, yPad) {
        const placed = [];
        let attempt = 0;
        while (placed.length < count && attempt < tries) {
            attempt += 1;
            const x = (Math.random() * 2 - 1) * (WORLD_HALF + 2);
            const z = (Math.random() * 2 - 1) * (WORLD_HALF + 2);
            if (pathBlend(x, z) < 0.6) { continue; }
            if (Math.hypot(x, z) < 4.5) { continue; }
            if (Math.hypot(x - NPC_POSITION.x, z - NPC_POSITION.z) < 3.6) { continue; }
            const scale = minScale + Math.random() * (maxScale - minScale);
            const inst = proto.createInstance(proto.name + '-i' + placed.length);
            inst.position.set(x, terrainHeight(x, z) + yPad * scale, z);
            inst.rotation.y = Math.random() * Math.PI * 2;
            inst.scaling.set(scale, scale * (0.85 + Math.random() * 0.5), scale);
            placed.push(inst);
        }
        return placed;
    }

    function buildScene(canvas) {
        scene = new BABYLON.Scene(engine);
        const horizon = new BABYLON.Color3(0.62, 0.70, 0.78);
        scene.clearColor = new BABYLON.Color4(horizon.r, horizon.g, horizon.b, 1);
        scene.fogMode = BABYLON.Scene.FOGMODE_EXP2;
        scene.fogColor = horizon;
        scene.fogDensity = 0.014;
        scene.ambientColor = new BABYLON.Color3(0.3, 0.33, 0.35);

        const hemi = new BABYLON.HemisphericLight('hemi', new BABYLON.Vector3(0, 1, 0), scene);
        hemi.intensity = 0.65;
        hemi.groundColor = new BABYLON.Color3(0.35, 0.42, 0.30);
        const sun = new BABYLON.DirectionalLight('sun', new BABYLON.Vector3(-0.4, -1, 0.6), scene);
        sun.position = new BABYLON.Vector3(20, 32, -24);
        sun.intensity = 1.15;
        sun.diffuse = new BABYLON.Color3(1, 0.96, 0.88);

        skyDome();
        buildTerrain();

        const treeProto = BABYLON.MeshBuilder.CreateCylinder('treeProto',
            { diameterTop: 0, diameterBottom: 1.7, height: 3.6, tessellation: 7 }, scene);
        const treeMat = new BABYLON.StandardMaterial('treeMat', scene);
        treeMat.diffuseColor = new BABYLON.Color3(0.11, 0.32, 0.15);
        treeMat.specularColor = new BABYLON.Color3(0.02, 0.02, 0.02);
        treeProto.material = treeMat;
        treeProto.isVisible = false;
        scatterOnTerrain(treeProto, 36, 400, 0.7, 1.7, 1.7);

        const rockProto = BABYLON.MeshBuilder.CreateIcoSphere('rockProto', { radius: 0.6, subdivisions: 1 }, scene);
        const rockMat = new BABYLON.StandardMaterial('rockMat', scene);
        rockMat.diffuseColor = new BABYLON.Color3(0.44, 0.42, 0.40);
        rockMat.specularColor = new BABYLON.Color3(0.03, 0.03, 0.03);
        rockProto.material = rockMat;
        rockProto.isVisible = false;
        scatterOnTerrain(rockProto, 14, 200, 0.5, 1.4, 0.25);

        player = BABYLON.MeshBuilder.CreateSphere('player', { diameter: 1.1, segments: 16 }, scene);
        player.position = new BABYLON.Vector3(0, terrainHeight(0, 0) + 0.55, 0);
        const playerMat = new BABYLON.StandardMaterial('playerMat', scene);
        playerMat.diffuseColor = new BABYLON.Color3(0.22, 0.55, 0.95);
        player.material = playerMat;

        const npc = BABYLON.MeshBuilder.CreateCylinder('npc', { diameterTop: 0, diameterBottom: 1.4, height: 2.4, tessellation: 12 }, scene);
        npc.position = new BABYLON.Vector3(NPC_POSITION.x, terrainHeight(NPC_POSITION.x, NPC_POSITION.z) + 1.2, NPC_POSITION.z);
        const npcMat = new BABYLON.StandardMaterial('npcMat', scene);
        npcMat.diffuseColor = new BABYLON.Color3(0.85, 0.62, 0.16);
        npcMat.emissiveColor = new BABYLON.Color3(0.25, 0.16, 0.02);
        npc.material = npcMat;

        const marker = BABYLON.MeshBuilder.CreateTorus('npcMarker', { diameter: 2.4, thickness: 0.12, tessellation: 32 }, scene);
        marker.position = new BABYLON.Vector3(NPC_POSITION.x, terrainHeight(NPC_POSITION.x, NPC_POSITION.z) + 0.08, NPC_POSITION.z);
        const markerMat = new BABYLON.StandardMaterial('markerMat', scene);
        markerMat.emissiveColor = new BABYLON.Color3(0.95, 0.75, 0.2);
        marker.material = markerMat;
        scene.registerBeforeRender(() => { marker.rotation.y += 0.02; });

        camera = new BABYLON.ArcRotateCamera('camera', -Math.PI / 2, Math.PI / 3.2, 15, player.position.clone(), scene);
        camera.lowerRadiusLimit = 6;
        camera.upperRadiusLimit = 32;
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

    function drawRemoteLabel(texture, name) {
        texture.clear();
        texture.drawText(String(name || 'Jogador').slice(0, 28), 12, 70, 'bold 42px sans-serif', '#ffffff', 'transparent', true);
    }

    function addRemote(id, name) {
        if (remotes.has(id) || !scene) { return; }
        const serial = remoteSerial++;
        const avatar = BABYLON.MeshBuilder.CreateSphere('remote-' + id, { diameter: 1.15, segments: 12 }, scene);
        avatar.position = new BABYLON.Vector3((serial % 5 - 2) * 1.5, terrainHeight(0, -2) + 0.58, -2 - Math.floor(serial / 5) * 2);
        const mat = new BABYLON.StandardMaterial('remote-mat-' + id, scene);
        mat.diffuseColor = new BABYLON.Color3(0.76, 0.32, 0.92);
        mat.emissiveColor = new BABYLON.Color3(0.10, 0.025, 0.14);
        avatar.material = mat;

        const label = BABYLON.MeshBuilder.CreatePlane('remote-label-' + id, { width: 2.8, height: 0.55 }, scene);
        label.position = avatar.position.add(new BABYLON.Vector3(0, 1.0, 0));
        label.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;
        const texture = new BABYLON.DynamicTexture('remote-name-' + id, { width: 512, height: 100 }, scene, false);
        texture.hasAlpha = true;
        drawRemoteLabel(texture, name);
        const labelMat = new BABYLON.StandardMaterial('remote-label-mat-' + id, scene);
        labelMat.diffuseTexture = texture;
        labelMat.emissiveColor = new BABYLON.Color3(1, 1, 1);
        labelMat.opacityTexture = texture;
        label.material = labelMat;
        remotes.set(id, { avatar: avatar, label: label, material: mat, texture: texture, labelMat: labelMat });
        if (dotnet) { dotnet.invokeMethodAsync('OnRemoteCount', remotes.size); }
    }

    function renameRemote(id, name) {
        const p = remotes.get(id);
        if (!p) { return; }
        drawRemoteLabel(p.texture, name);
    }

    function updateRemote(id, x, z, ry) {
        const p = remotes.get(id);
        if (!p) { return; }
        p.avatar.position.x = Math.max(-WORLD_HALF, Math.min(WORLD_HALF, x));
        p.avatar.position.z = Math.max(-WORLD_HALF, Math.min(WORLD_HALF, z));
        p.avatar.position.y = terrainHeight(p.avatar.position.x, p.avatar.position.z) + 0.58;
        p.avatar.rotation.y = ry;
        p.label.position.x = p.avatar.position.x;
        p.label.position.z = p.avatar.position.z;
        p.label.position.y = p.avatar.position.y + 1.0;
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
                if (player) { player.position.y = terrainHeight(player.position.x, player.position.z) + 0.55; }
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
        renameRemote,
        updateRemote,
        removeRemote
    };
})();
