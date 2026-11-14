// Ultima — camada de renderização (M1).
// Babylon.js desenha o mundo 3D usando a GPU; C# (Blazor) cuida do HUD e do diálogo.
// Marco futuro: LLM local via WebGPU para as falas do NPC e multiplayer P2P (WebRTC).
(function () {
    'use strict';

    const WORLD_HALF = 24;          // meia largura do mundo andável
    const NPC_POSITION = { x: 0, z: -14 }; // guardiã no "norte" da trilha
    const NEAR_RADIUS = 3.2;        // distância para abrir conversa
    const SPEED = 6;                // unidades por segundo
    const TICK_MS = 200;            // frequência do repasse de posição para o HUD

    let engine = null;
    let scene = null;
    let player = null;
    let camera = null;
    let dotnet = null;
    let near = false;
    let frozen = false;
    let lastTick = 0;
    const keys = Object.create(null);

    function isGameKey(code) {
        return ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(code);
    }

    function onKeyDown(e) {
        if (isGameKey(e.code)) {
            keys[e.code] = true;
            e.preventDefault();
            return;
        }
        if (e.code === 'KeyE') {
            e.preventDefault();
            if (dotnet) { dotnet.invokeMethodAsync('OnInteract'); }
        } else if (e.code === 'Escape') {
            e.preventDefault();
            if (dotnet) { dotnet.invokeMethodAsync('OnClose'); }
        }
    }

    function onKeyUp(e) {
        if (isGameKey(e.code)) {
            keys[e.code] = false;
            e.preventDefault();
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
        ground.receiveShadows = false;

        const path = BABYLON.MeshBuilder.CreateBox('path', { width: 2.2, depth: WORLD_HALF * 2 }, scene);
        path.position.y = 0.02;
        const pathMat = new BABYLON.StandardMaterial('pathMat', scene);
        pathMat.diffuseColor = new BABYLON.Color3(0.42, 0.34, 0.24);
        pathMat.specularColor = new BABYLON.Color3(0.02, 0.02, 0.02);
        path.material = pathMat;

        player = BABYLON.MeshBuilder.CreateSphere('player', { diameter: 1.1, segments: 20 }, scene);
        player.position = new BABYLON.Vector3(0, 0.55, 0);
        const playerMat = new BABYLON.StandardMaterial('playerMat', scene);
        playerMat.diffuseColor = new BABYLON.Color3(0.22, 0.55, 0.95);
        playerMat.specularColor = new BABYLON.Color3(0.3, 0.3, 0.3);
        player.material = playerMat;

        const npc = BABYLON.MeshBuilder.CreateCylinder('npc', { diameterTop: 0, diameterBottom: 1.4, height: 2.4, tessellation: 16 }, scene);
        npc.position = new BABYLON.Vector3(NPC_POSITION.x, 1.2, NPC_POSITION.z);
        const npcMat = new BABYLON.StandardMaterial('npcMat', scene);
        npcMat.diffuseColor = new BABYLON.Color3(0.85, 0.62, 0.16);
        npcMat.emissiveColor = new BABYLON.Color3(0.25, 0.16, 0.02);
        npc.material = npcMat;
        npc.metadata = { npc: true };

        const marker = BABYLON.MeshBuilder.CreateTorus('npcMarker', { diameter: 2.4, thickness: 0.12, tessellation: 40 }, scene);
        marker.position = new BABYLON.Vector3(NPC_POSITION.x, 0.08, NPC_POSITION.z);
        const markerMat = new BABYLON.StandardMaterial('markerMat', scene);
        markerMat.emissiveColor = new BABYLON.Color3(0.95, 0.75, 0.2);
        marker.material = markerMat;
        scene.registerBeforeRender(() => {
            marker.rotation.y += 0.02;
        });

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

    function movePlayer(deltaSeconds) {
        if (frozen || !player) {
            return;
        }
        let ix = 0;
        let iz = 0;
        if (keys.KeyW || keys.ArrowUp) { iz += 1; }
        if (keys.KeyS || keys.ArrowDown) { iz -= 1; }
        if (keys.KeyA || keys.ArrowLeft) { ix -= 1; }
        if (keys.KeyD || keys.ArrowRight) { ix += 1; }
        if (ix === 0 && iz === 0) {
            return;
        }

        // Movimento relativo à câmera, projetado no plano XZ.
        const forward = camera.getForwardRay().direction;
        const fx = forward.x, fz = forward.z;
        const flen = Math.hypot(fx, fz) || 1;
        const nx = fz / flen, nz = -fx / flen; // esquerda da câmera

        let dx = (fx / flen) * iz + nx * ix;
        let dz = (fz / flen) * iz + nz * ix;
        const len = Math.hypot(dx, dz) || 1;
        player.position.x += (dx / len) * SPEED * deltaSeconds;
        player.position.z += (dz / len) * SPEED * deltaSeconds;
        clampToWorld(player);
        player.rotation.y = Math.atan2(dx, dz);
    }

    function checkNear() {
        if (!player) {
            return;
        }
        const dist = Math.hypot(player.position.x - NPC_POSITION.x, player.position.z - NPC_POSITION.z);
        const nowNear = dist < NEAR_RADIUS;
        if (nowNear !== near) {
            near = nowNear;
            if (dotnet) { dotnet.invokeMethodAsync('OnNearChanged', nowNear); }
        }
    }

    function tick(now) {
        const nowMs = now * 1000;
        if (nowMs - lastTick >= TICK_MS) {
            lastTick = nowMs;
            if (dotnet && player) {
                dotnet.invokeMethodAsync('OnMoveTick', player.position.x, player.position.z, engine.getFps());
            }
        }
    }

    window.game = {
        init(canvasId, dotnetRef) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) {
                throw new Error('canvas ' + canvasId + ' não encontrado');
            }
            dotnet = dotnetRef;

            engine = new BABYLON.Engine(canvas, true, { adaptToDeviceRatio: true, stencil: false });
            buildScene(canvas);

            window.addEventListener('keydown', onKeyDown);
            window.addEventListener('keyup', onKeyUp);
            window.addEventListener('blur', () => {
                for (const k of Object.keys(keys)) { keys[k] = false; }
            });

            engine.runRenderLoop(() => {
                const delta = engine.getDeltaTime() / 1000;
                movePlayer(delta);
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
                    if (glInfo && (glInfo.renderer || glInfo.vendor)) {
                        rendererName = (glInfo.renderer || glInfo.vendor);
                    }
                } catch (e) {
                    rendererName = 'WebGL';
                }
                if (dotnet) { dotnet.invokeMethodAsync('OnEngineReady', String(rendererName)); }
            });

            canvas.focus();
            return true;
        },

        setFrozen(value) {
            frozen = !!value;
            if (frozen) {
                for (const k of Object.keys(keys)) { keys[k] = false; }
            }
        },

        resetPlayer() {
            if (player) {
                player.position.x = 0;
                player.position.z = 0;
                player.rotation.y = 0;
            }
            if (camera) {
                camera.alpha = -Math.PI / 2;
                camera.beta = Math.PI / 3.2;
            }
        }
    };
})();
