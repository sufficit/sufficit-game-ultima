// UltimaCore — motor do Ultima (cena Babylon.js: terreno procedural, fila de modelos
// com orçamento por frame, assets GLB CC0, NPCs visuais) SEM dependência de DOM:
// roda na main thread (fallback) ou em Web Worker com OffscreenCanvas (game-worker.js).
// host = { canvas, mode: 'main'|'worker', build, global, dom, postEvent, sendPose, onInputMode }.
(function () {
    'use strict';
    const ROOT = (typeof window !== 'undefined') ? window : self;   // main × worker
    ROOT.UltimaCore = {
        create: function (host) {

    const WORLD_HALF = 48;
    const NPC_POSITION = { x: 0, z: -14 };      // Guardiã
    const WANDERER_HOME = { x: 5.0, z: 0.0 };    // Andarilho: circula pela clareira inicial
    // Escala realista: personagem com ~1,75 m; os olhos ficam a ~1,62 m do chão.
    const NEAR_RADIUS = 3.2;
    const WALK_SPEED = 3.5;   // m/s (caminhada rápida)
    const RUN_SPEED = 6.5;    // m/s (corrida leve, Shift ou analógico no fim do curso)
    const PLAYER_HEIGHT = 1.75;
    // Alvo da câmera fica na altura do peito (o root do personagem agora é os pés).
    const CAM_UP = new BABYLON.Vector3(0, PLAYER_HEIGHT / 2, 0);
    const PLAYER_RADIUS = 0.25;
    const TICK_MS = 200;
    const DEADZONE = 0.18;
    // Orçamento de CPU por frame para subir modelos (texturas, malhas, scatter). Referência:
    // lineage-rust amortiza a entrada de personagens com 4 ms/frame (CHARACTER_BUDGET_MS).
    const MODEL_BUDGET_MS = 4.0;
    const MODEL_SCATTER_BATCH = 24; // instâncias por passo de espalhamento

    let engine = null;
    let scene = null;
    let sceneShadows = null;
    let player = null;
    let camera = null;
    const G = host.global;
    const dom = !!host.dom;
    const CAM_POINTER_SENS = 0.008;   // rad/px do arrasto no modo worker (sem attachControl)
    const camPending = { dx: 0, dy: 0, wheel: 0 };
    const padState = { x: 0, y: 0, lookX: 0, lookY: 0 };
    // Eventos para fora (UI C#, rede) saem todos por aqui — idêntico nos dois modos.
    const dotnet = { invokeMethodAsync: function (name) { host.postEvent(name, Array.prototype.slice.call(arguments, 1)); } };
    // No worker, caminhos relativos resolveriam contra /js/ (URL do script do worker):
    // os fetch do jogo são sempre relativos à página.
    const pageUrl = function (path) { return new URL(path, host.pageBase).href; };
    let near = false;
    let nearWanderer = false;
    let wandererRoot = null;
    let frozen = false;
    let lastTick = 0;
    let actionWasDown = false;
    let moveStick = { x: 0, y: 0 };
    let lookStick = { x: 0, y: 0 };
    let remoteSerial = 0;
    const remotes = new Map();
    const keys = Object.create(null);

    const gameCodes = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ShiftLeft', 'ShiftRight'];
    const editable = (el) => !!(el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName)));

    function interact() {
        if (dotnet) { dotnet.invokeMethodAsync('OnInteract'); }
    }

    function onKeyDown(e) {
        setActiveInput('keyboard');
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
            setActiveInput('touch');
            pointer = e.pointerId;
            try { host.setPointerCapture(pointer); } catch (err) { /* captura opcional */ }
            e.preventDefault();
            update(e);
        });
        host.addEventListener('pointermove', (e) => { if (e.pointerId === pointer) { update(e); } });
        host.addEventListener('pointerup', (e) => { if (e.pointerId === pointer) { reset(); } });
        host.addEventListener('pointercancel', (e) => { if (e.pointerId === pointer) { reset(); } });
        // Perda de captura ou de foco da janela não pode deixar o stick preso.
        host.addEventListener('lostpointercapture', (e) => { if (e.pointerId === pointer) { reset(); } });
        window.addEventListener('blur', reset);
        function update(e) {
            const r = host.getBoundingClientRect();
            const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            const radius = Math.min(r.width, r.height) * 0.37;
            let dx = (e.clientX - cx) / radius, dy = (e.clientY - cy) / radius;
            const length = Math.hypot(dx, dy);
            if (length > 1) { dx /= length; dy /= length; }
            // Zona morta radial: o polegar segue o dedo e o jogo recebe o vetor reescalado.
            const magnitude = Math.hypot(dx, dy);
            const scaled = magnitude <= DEADZONE ? 0 : Math.min(1, (magnitude - DEADZONE) / (1 - DEADZONE));
            thumb.style.transform = `translate(${dx * radius}px, ${dy * radius}px)`;
            const dir = magnitude > 0 ? scaled / magnitude : 0;
            set({ x: dx * dir, y: -dy * dir });
        }
        void get;
    }

    function setupTouchControls() {
        setupVirtualStick('move-stick', 'move-thumb', () => moveStick, (v) => { moveStick = v; });
        setupVirtualStick('look-stick', 'look-thumb', () => lookStick, (v) => { lookStick = v; });
        const action = document.getElementById('touch-action');
        if (action) {
            action.addEventListener('pointerdown', (e) => { setActiveInput('touch'); e.preventDefault(); interact(); });
        }
        const cancelBtn = document.getElementById('touch-cancel');
        if (cancelBtn) {
            cancelBtn.addEventListener('pointerdown', (e) => {
                setActiveInput('touch'); e.preventDefault();
                if (dotnet) { dotnet.invokeMethodAsync('OnClose'); }
            });
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
        const r = Math.hypot(x, z);
        // Montanhas em cristas no perímetro, subindo até ~25 m.
        const edge = r + noise(x * 0.07 - 51.7, z * 0.07 + 91.3) * 5.0;
        const rim = smoothstep(24, 45, edge);
        let h = Math.pow(rim, 1.5) * (21 + noise(x * 0.04 + 5, z * 0.04 - 5) * 7.0);
        h += rim * Math.abs(noise(x * 0.16 + 3, z * 0.16 - 8)) * 3.5;
        // Colinas suaves no vale e microrrelevo.
        h += noise(x * 0.03, z * 0.03) * 2.2;
        h += noise(x * 0.085 + 40.2, z * 0.085 - 17.4) * 0.6;
        h += noise(x * 0.3 - 9.3, z * 0.3 + 23.8) * 0.15;
        // A trilha aplaina o terreno, mas se dissolve nas montanhas.
        const flat = (1 - pathBlend(x, z)) * (1 - smoothstep(22, 34, r));
        h = lerp(h, Math.max(0, h * 0.12), flat * 0.9);
        // Clareiras planas no ponto de partida e junto da Guardiã.
        h = lerp(0.05, h, smoothstep(3.2, 9, r));
        const npcFlat = smoothstep(6, 3, Math.hypot(x - NPC_POSITION.x, z - NPC_POSITION.z));
        return lerp(h, 0.05, npcFlat * 0.9);
    }

    function slopeAt(x, z) {
        const e = 0.7;
        const dhx = Math.abs(terrainHeight(x + e, z) - terrainHeight(x - e, z));
        const dhz = Math.abs(terrainHeight(x, z + e) - terrainHeight(x, z - e));
        return Math.max(dhx, dhz) / (2 * e);
    }

    // ---------- Cena ----------
    // Distorce levemente vértices do protótipo: silhueta orgânica replicada nas instâncias.
    function jitterMesh(mesh, amount, seed) {
        const pos=mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind); if(!pos)return;
        const rnd=makeNoise(seed);
        for(let i=0;i<pos.length;i+=3){
            const x=pos[i],y=pos[i+1],z=pos[i+2]; if(Math.hypot(x,y,z)<1e-5)continue;
            const n=rnd(x*2.3+y*1.1,z*2.3-y*0.7),f=1+(n-0.5)*2*amount;
            pos[i]=x*f;pos[i+1]=y*f;pos[i+2]=z*f;
        }
        mesh.updateVerticesData(BABYLON.VertexBuffer.PositionKind,pos);
    }

    // Estria o tronco por ângulo, preservando veios paralelos à altura.
    function fluteMesh(mesh, amount, seed) {
        const pos=mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind); if(!pos)return;
        const rnd=makeNoise(seed);
        for(let i=0;i<pos.length;i+=3){
            const x=pos[i],z=pos[i+2];if(Math.hypot(x,z)<1e-5)continue;
            const a=Math.atan2(z,x),n=rnd(Math.cos(a)*3+5,Math.sin(a)*3+5),f=1+(n-0.5)*2*amount;
            pos[i]=x*f;pos[i+2]=z*f;
        }
        mesh.updateVerticesData(BABYLON.VertexBuffer.PositionKind,pos);
    }

    function skyDome() {
        const sky = BABYLON.MeshBuilder.CreateSphere('sky', { diameter: 180, segments: 12, sideOrientation: BABYLON.Mesh.BACKSIDE }, scene);
        sky.infiniteDistance = true;
        sky.isPickable = false;
        const texture = new BABYLON.DynamicTexture('skyTex', { width: 64, height: 512 }, scene, false);
        const ctx = texture.getContext();
        const grad = ctx.createLinearGradient(0, 0, 0, 512);
        grad.addColorStop(0, '#4d7dc4');
        grad.addColorStop(0.52, '#9fc0e0');
        grad.addColorStop(0.75, '#d3e2ee');
        grad.addColorStop(1, '#eef1f2');
        ctx.fillStyle = grad;
        ctx.fillRect(0, 0, 64, 512);
        // Nuvens translúcidas distribuídas na faixa do horizonte.
        for (let i = 0; i < 46; i++) {
            const cx = Math.random() * 64;
            const cy = 60 + Math.random() * 230;
            const s = 5 + Math.random() * 13;
            ctx.fillStyle = 'rgba(255,255,255,' + (0.10 + Math.random() * 0.22).toFixed(2) + ')';
            ctx.beginPath();
            ctx.ellipse(cx, cy, s, s * 0.42, 0, 0, Math.PI * 2);
            ctx.fill();
        }
        texture.update(false);
        const mat = new BABYLON.StandardMaterial('skyMat', scene);
        mat.disableLighting = true;
        mat.emissiveTexture = texture;
        mat.diffuseColor = new BABYLON.Color3(0, 0, 0);
        mat.specularColor = new BABYLON.Color3(0, 0, 0);
        mat.fogEnabled = false;
        sky.material = mat;
    }

    // ---------- Texturas procedurais com cache OPFS (game.assets) ----------
    // A geração (loops de pixels) é o custo real de CPU; o artefato pronto (RGBA e,
    // quando existe, o heightmap _h) é cacheado por SHA-256 de {build, nome, tamanho}:
    // a primeira visita gera e grava, as seguintes carregam prontas (warm no init).
    // Sem OPFS — ou com cache corrompido — gera como antes: o cache é pura otimização.
    const texCache = new Map();   // nome -> { size, rgba, h }
    const texWarm = { bark: 128, foliage: 128, rock: 128, grass: 128, detail: 256 };

    function applyTextureData(name, size, data) {
        const tex = new BABYLON.DynamicTexture(name, { width: size, height: size }, scene, true);
        tex.getContext().putImageData(new ImageData(data.rgba, size, size), 0, 0);
        tex.update(true);
        if (data.h) { tex._h = data.h; }
        return tex;
    }

    function proceduralTexture(name, size, generate) {
        let data = texCache.get(name);
        if (!data) {
            data = generate(size);
            texCache.set(name, data);
            if (G.game && G.game.assets) { G.game.assets.saveTex(name, size, data.rgba, data.h); }
        }
        return applyTextureData(name, size, data);
    }

    async function warmTextures() {
        if (!(G.game && G.game.assets && G.game.assets.report().opfs)) { return; }
        for (const name of Object.keys(texWarm)) {
            if (texCache.has(name)) { continue; }
            const hit = await game.assets.loadTex(name, texWarm[name]);
            if (hit && hit.rgba.length === texWarm[name] * texWarm[name] * 4) {
                texCache.set(name, { size: hit.size, rgba: hit.rgba, h: hit.h });
            }
        }
    }

    // Granulação de solo/grama (multiplica a cor por vértice); ladrilha sem emenda visível.
    function makeDetailTexture() {
        const tex = proceduralTexture('detail', 256, function (N) {
            const img = new ImageData(N, N);
            const rnd = makeNoise(777);
            for (let y = 0; y < N; y++) {
                for (let x = 0; x < N; x++) {
                    // Ruído periódico: soma de ruídos amostrados em círculo (sem costura).
                    const u = x / N * Math.PI * 2, v = y / N * Math.PI * 2;
                    const c1 = rnd(Math.cos(u) * 3 + 10, Math.sin(u) * 3 + Math.cos(v) * 3);
                    const c2 = rnd(Math.cos(u * 3) * 4 - 7, Math.sin(v * 3) * 4 + Math.sin(u * 3) * 4);
                    const grain = (Math.random() - 0.5) * 0.22;
                    const val = 0.84 + c1 * 0.10 + c2 * 0.07 + grain;
                    const k = Math.max(0, Math.min(255, val * 255)) | 0;
                    const i = (y * N + x) * 4;
                    img.data[i] = k; img.data[i + 1] = k; img.data[i + 2] = Math.min(255, k + 2); img.data[i + 3] = 255;
                }
            }
            return { rgba: img.data, h: null };
        });
        tex.anisotropicFilteringLevel = 8;
        return tex;
    }

    const GROUND_SUBDIVISIONS = 192;
    // Malha do terreno (posições/cores/normais) em cache OPFS: chave por {build, nº de
    // vértices}. Sem cache, computa o ruído por vértice como antes e grava o artefato
    // pronto para a próxima visita (async, sem travar a carga).
    let terrainCache = null;
    async function warmTerrain() {
        if (!(G.game && G.game.assets && G.game.assets.report().opfs)) { return; }
        const vc = (GROUND_SUBDIVISIONS + 1) * (GROUND_SUBDIVISIONS + 1);
        const hit = await game.assets.loadTerrain(vc);
        if (hit && hit.positions.length === vc * 3 && hit.colors.length === vc * 4 && hit.normals.length === vc * 3) {
            terrainCache = hit;
        }
    }

    function buildTerrain() {
        const size = WORLD_HALF * 2 + 8;
        const ground = BABYLON.MeshBuilder.CreateGround('ground', { width: size, height: size, subdivisions: GROUND_SUBDIVISIONS }, scene);
        if (terrainCache) {
            ground.setVerticesData(BABYLON.VertexBuffer.PositionKind, terrainCache.positions);
            ground.setVerticesData(BABYLON.VertexBuffer.ColorKind, terrainCache.colors);
            ground.setVerticesData(BABYLON.VertexBuffer.NormalKind, terrainCache.normals);
            const groundMatReady = groundMaterial();
            ground.material = groundMatReady;
            ground.receiveShadows = true;
            return ground;
        }
        const positions = ground.getVerticesData(BABYLON.VertexBuffer.PositionKind);
        const colors = new Float32Array((positions.length / 3) * 4);

        // Paleta: grama em duas tonalidades, terra da trilha, rocha úmida e neve.
        const grassDark = [0.16, 0.34, 0.13];
        const grassLight = [0.38, 0.52, 0.20];
        const grassDry = [0.55, 0.55, 0.26];
        const dirt = [0.47, 0.36, 0.23];
        const dirtLight = [0.58, 0.47, 0.32];
        const rock = [0.42, 0.41, 0.40];
        const rockLight = [0.55, 0.54, 0.52];
        const snow = [0.93, 0.95, 0.97];

        for (let i = 0; i < positions.length; i += 3) {
            const x = positions[i], z = positions[i + 2];
            const y = terrainHeight(x, z);
            positions[i + 1] = y;

            // Manchas de grama seca e variação fina de tom (quebra a monotonia).
            const patch = noise(x * 0.11 + 100, z * 0.11 - 60);
            const fine = noise(x * 0.85 - 13.7, z * 0.85 + 77.1) * 0.5 + 0.5;
            const t = Math.max(0, Math.min(1, y * 0.16 + 0.42 + patch * 0.45 + (fine - 0.5) * 0.18));
            let r = lerp(lerp(grassDark[0], grassLight[0], t), grassDry[0], Math.max(0, patch - 0.35) * 1.4);
            let g = lerp(lerp(grassDark[1], grassLight[1], t), grassDry[1], Math.max(0, patch - 0.35) * 1.4);
            let b = lerp(lerp(grassDark[2], grassLight[2], t), grassDry[2], Math.max(0, patch - 0.35) * 1.4);

            // Trilha de terra batida, mais clara no centro.
            const path = pathBlend(x, z);
            const core = 1 - smoothstep(0.4, 2.6, Math.abs(x - pathOffset(z)));
            const dr = lerp(dirtLight[0], dirt[0], path), dg = lerp(dirtLight[1], dirt[1], path), db = lerp(dirtLight[2], dirt[2], path);
            r = lerp(dr, r, path); g = lerp(dg, g, path); b = lerp(db, b, path);
            r = lerp(r, dr * 1.08, (1 - path) * core * 0.5);
            g = lerp(g, dg * 1.08, (1 - path) * core * 0.5);
            b = lerp(b, db * 1.08, (1 - path) * core * 0.5);

            // Encostas íngremes viram rocha exposta.
            const slope = slopeAt(x, z);
            const rocky = Math.max(0, Math.min(1, (slope - 0.55) / 0.55));
            const rr = lerp(rock[0], rockLight[0], fine);
            r = lerp(r, rr, rocky); g = lerp(g, lerp(rock[1], rockLight[1], fine), rocky); b = lerp(b, lerp(rock[2], rockLight[2], fine), rocky);

            // Neve acima de ~8,5 m, avançando antes nos pontos abrigados do ruído.
            const snowLine = 8.5 + noise(x * 0.18 + 7.7, z * 0.18 - 3.3) * 1.6 - rocky * 1.2;
            const snowy = Math.max(0, Math.min(1, (y - snowLine) / 1.6));
            r = lerp(r, snow[0], snowy); g = lerp(g, snow[1], snowy); b = lerp(b, snow[2], snowy);

            const ci = (i / 3) * 4;
            colors[ci] = r; colors[ci + 1] = g; colors[ci + 2] = b; colors[ci + 3] = 1;
        }
        ground.setVerticesData(BABYLON.VertexBuffer.PositionKind, positions);
        ground.setVerticesData(BABYLON.VertexBuffer.ColorKind, colors);
        const normals = new Float32Array(positions.length);
        BABYLON.VertexData.ComputeNormals(positions, ground.getIndices(), normals);
        ground.setVerticesData(BABYLON.VertexBuffer.NormalKind, normals);
        if (G.game && G.game.assets) { G.game.assets.saveTerrain(positions.length / 3, positions, colors, normals); }

        ground.material = groundMaterial();
        ground.receiveShadows = true;
        return ground;
    }

    // Material do chão (granulação em ladrilhos de ~3 m), compartilhado pelos dois
    // caminhos do buildTerrain (cacheado ou computado).
    function groundMaterial() {
        const size = WORLD_HALF * 2 + 8;
        const groundMat = new BABYLON.StandardMaterial('groundMat', scene);
        groundMat.diffuseColor = new BABYLON.Color3(1, 1, 1);
        groundMat.specularColor = new BABYLON.Color3(0.02, 0.02, 0.02);
        const detail = makeDetailTexture();
        detail.uScale = size / 3; detail.vScale = size / 3;   // um ladrilho a cada ~3 m
        groundMat.diffuseTexture = detail;
        return groundMat;
    }

    // ---------- v3: relevo (normal map) e oclusão ambiente nos vértices ----------
    // Converte o mapa de alturas guardado em tex._h num mapa de normais (relevo real sob a luz).
    function makeBumpTexture(srcTex, N, strength) {
        const h = srcTex._h, tex = new BABYLON.DynamicTexture('bump-' + srcTex.name, { width: N, height: N }, scene, true);
        const ctx = tex.getContext(), img = ctx.createImageData(N, N);
        const at = (x, y) => h[((y + N) % N) * N + ((x + N) % N)];
        for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
            const dx = (at(x + 1, y) - at(x - 1, y)) * strength, dy = (at(x, y + 1) - at(x, y - 1)) * strength;
            const inv = 1 / Math.hypot(dx, dy, 1), i = (y * N + x) * 4;
            img.data[i] = ((-dx * inv) * 0.5 + 0.5) * 255; img.data[i + 1] = ((-dy * inv) * 0.5 + 0.5) * 255;
            img.data[i + 2] = (inv * 0.5 + 0.5) * 255; img.data[i + 3] = 255;
        }
        ctx.putImageData(img, 0, 0); tex.update(true); return tex;
    }

    // AO "assado": escurece a base e variações por vértice (copas mais densas por baixo).
    function bakeVertexAO(mesh, darkest, seed) {
        const pos = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
        let minY = Infinity, maxY = -Infinity;
        for (let i = 1; i < pos.length; i += 3) { minY = Math.min(minY, pos[i]); maxY = Math.max(maxY, pos[i]); }
        const rnd = makeNoise(seed), col = new Float32Array((pos.length / 3) * 4);
        for (let v = 0; v < pos.length / 3; v++) {
            const t = (pos[v * 3 + 1] - minY) / Math.max(0.001, maxY - minY);
            const k = darkest + (1 - darkest) * Math.pow(t, 0.7) + (rnd(pos[v * 3] * 1.7, pos[v * 3 + 2] * 1.7) - 0.5) * 0.12;
            const c = Math.max(0.2, Math.min(1.05, k));
            col[v * 4] = c; col[v * 4 + 1] = c; col[v * 4 + 2] = c * 0.97; col[v * 4 + 3] = 1;
        }
        mesh.setVerticesData(BABYLON.VertexBuffer.ColorKind, col);
    }

    // ---------- Texturas procedurais de vegetação (cache OPFS via proceduralTexture) ----------
    // Casca: veios verticais irregulares, sem costura na horizontal.
    function makeBarkTexture() {
        return proceduralTexture('bark', 128, function (W) {
            const H = W;
            const img = new ImageData(W, H), rnd = makeNoise(421), hgt = new Float32Array(W * H);
            for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
                const u = x / W * Math.PI * 2;
                const ridge = rnd(Math.cos(u) * 6 + 3, Math.sin(u) * 6 + 3);
                const fibre = rnd(Math.cos(u) * 14 - 9, y * 0.35 + Math.sin(u) * 14);
                const val = 0.52 + ridge * 0.30 + (fibre - 0.5) * 0.22, i = (y * W + x) * 4;
                hgt[y * W + x] = val; img.data[i] = Math.max(0, Math.min(255, val * 190)) | 0;
                img.data[i+1] = Math.max(0, Math.min(255, val * 135)) | 0;
                img.data[i+2] = Math.max(0, Math.min(255, val * 88)) | 0; img.data[i+3] = 255;
            }
            return { rgba: img.data, h: hgt };
        });
    }

    // Folhagem: manchas procedurais de verde, em vez de uma cor plana.
    function makeFoliageTexture() {
        return proceduralTexture('foliage', 128, function (N) {
            const img = new ImageData(N, N), rnd = makeNoise(907);
            for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
                const u = x / N * Math.PI * 2, v = y / N * Math.PI * 2;
                const blob = rnd(Math.cos(u)*5+1, Math.sin(u)*5+Math.cos(v)*5);
                const leaf = rnd(Math.cos(u)*16+8, Math.sin(v)*16-4), t = blob*0.7+leaf*0.3, i=(y*N+x)*4;
                img.data[i] = Math.max(0,Math.min(255,(0.10+t*0.22)*255))|0;
                img.data[i+1] = Math.max(0,Math.min(255,(0.26+t*0.42)*255))|0;
                img.data[i+2] = Math.max(0,Math.min(255,(0.10+t*0.16)*255))|0; img.data[i+3]=255;
            }
            return { rgba: img.data, h: null };
        });
    }

    // Rocha com variação de cor e salpicos minerais procedurais.
    function makeRockTexture() {
        return proceduralTexture('rock', 128, function (N) {
            const img = new ImageData(N, N), rnd = makeNoise(577), hgt = new Float32Array(N * N);
            for(let y=0;y<N;y++) for(let x=0;x<N;x++) {
                const u=x/N*Math.PI*2,v=y/N*Math.PI*2;
                const blotch=rnd(Math.cos(u)*5-3,Math.sin(v)*5+4), speck=rnd(Math.cos(u)*18+6,Math.sin(v)*18-7);
                const val=0.58+blotch*0.24+(speck-0.5)*0.10,i=(y*N+x)*4;
                hgt[y*N+x]=val; img.data[i]=Math.max(0,Math.min(255,val*118))|0;
                img.data[i+1]=Math.max(0,Math.min(255,val*114))|0;
                img.data[i+2]=Math.max(0,Math.min(255,val*108))|0; img.data[i+3]=255;
            }
            return { rgba: img.data, h: hgt };
        });
    }

    function makeGrassTexture() {
        const tex = proceduralTexture('grass', 128, function (N) {
            const canvas = (typeof OffscreenCanvas !== 'undefined') ? new OffscreenCanvas(N, N) : document.createElement('canvas');
            canvas.width = N; canvas.height = N;
            const ctx = canvas.getContext('2d');
            ctx.clearRect(0, 0, N, N);
            const tones = ['#3d6b21', '#4d7d2a', '#5d8f33', '#35591c'];
            for (let i = 0; i < 16; i++) {
                const x0 = 6 + Math.random() * (N - 12);
                const h = 40 + Math.random() * 70;
                const bend = (Math.random() - 0.5) * 34;
                ctx.strokeStyle = tones[(Math.random() * tones.length) | 0];
                ctx.lineWidth = 2 + Math.random() * 2;
                ctx.lineCap = 'round';
                ctx.beginPath();
                ctx.moveTo(x0, N);
                ctx.quadraticCurveTo(x0 + bend * 0.4, N - h * 0.6, x0 + bend, N - h);
                ctx.stroke();
            }
            return { rgba: ctx.getImageData(0, 0, N, N).data, h: null };
        });
        tex.hasAlpha = true;
        return tex;
    }

    // Tufo de grama: dois planos cruzados com lâminas desenhadas (transparência).
    function buildGrassPatch() {
        const quad = BABYLON.MeshBuilder.CreatePlane('grassQuad', { width: 0.8, height: 0.45 }, scene);
        const mat = new BABYLON.StandardMaterial('grassMat', scene);
        mat.diffuseTexture = makeGrassTexture();
        mat.useAlphaFromDiffuseTexture = true;
        mat.specularColor = new BABYLON.Color3(0, 0, 0);
        mat.backFaceCulling = false;
        quad.material = mat;
        quad.position.y = 0.225;
        const q2 = quad.clone('grassQuad2');
        q2.rotation.y = Math.PI / 2;
        const merged = BABYLON.Mesh.MergeMeshes([quad, q2], true, true, undefined, false, true);
        merged.isVisible = false;
        merged.isPickable = false;
        return merged;
    }

    // Árvores: espalha instâncias de um modelo carregado (descritor C#). Todas as partes do
    // modelo têm origem no ponto de apoio (chão) e recebem a mesma escala, então o encaixe
    // entre tronco e copa garantido pelo Ultima.Models.Core se mantém em qualquer tamanho.
    // Resumível: cada chamada coloca até "n" árvores e devolve true ao terminar, para a fila
    // de modelos espalhar em lotes sem estourar o orçamento do frame.
    function makeTreePlacer(protos, sink, count) {
        let placed = 0, tries = 0;
        return function (n) {
            while (placed < count && tries < 2400 && n > 0) {
                tries += 1;
                const x = (Math.random() * 2 - 1) * (WORLD_HALF + 4);
                const z = (Math.random() * 2 - 1) * (WORLD_HALF + 4);
                const r = Math.hypot(x, z);
                if (r < 6.5) { continue; }                                                     // clareira inicial
                if (Math.hypot(x - NPC_POSITION.x, z - NPC_POSITION.z) < 5.5) { continue; }    // clareira da Guardiã
                if (pathBlend(x, z) < 0.75) { continue; }                                      // longe da trilha
                const y = terrainHeight(x, z);
                if (y > 17) { continue; }                                                      // acima da linha das árvores
                if (slopeAt(x, z) > 0.5) { continue; }                                         // encosta íngreme
                const s = 0.75 + Math.random() * 0.9;
                const sy = s * (0.9 + Math.random() * 0.5);
                for (const proto of protos) {
                    const inst = proto.createInstance(proto.name.replace('model-', 'tree-') + '-' + placed);
                    inst.position.set(x, y - sink * sy, z);
                    inst.scaling.set(s, sy, s);
                    inst.rotation.y = Math.random() * Math.PI * 2;
                }
                placed += 1;
                n -= 1;
            }
            return placed >= count || tries >= 2400;
        };
    }

    // ---------- Renderizador genérico de modelos (descritores do Ultima.Models.Core) ----------
    const modelTextures = {};
    function modelMaterial(def) {
        const mat = new BABYLON.StandardMaterial('mat-' + def.id + '-' + Math.random().toString(36).slice(2, 6), scene);
        const makers = { bark: makeBarkTexture, foliage: makeFoliageTexture, rock: makeRockTexture, grass: makeGrassTexture };
        if (makers[def.texture]) {
            const tex = modelTextures[def.texture] || (modelTextures[def.texture] = makers[def.texture]());
            mat.diffuseTexture = tex;
            if (def.bumpStrength > 0 && tex._h) { mat.bumpTexture = makeBumpTexture(tex, 128, def.bumpStrength); mat.bumpTexture.level = 1.1; }
            if (def.uScale && def.uScale !== 1) { mat.diffuseTexture.uScale = def.uScale; }
            if (def.texture === 'grass') { mat.useAlphaFromDiffuseTexture = true; mat.backFaceCulling = false; }  // lâminas com alfa nos dois lados
        }
        mat.diffuseColor = new BABYLON.Color3(def.color[0], def.color[1], def.color[2]);
        mat.emissiveColor = new BABYLON.Color3(def.emissive[0], def.emissive[1], def.emissive[2]);
        mat.specularColor = new BABYLON.Color3(def.specular, def.specular, def.specular);
        return mat;
    }

    // Cria um protótipo invisível de UM grupo do descritor (origem = chão do modelo).
    // Um grupo por passo: a fila de modelos corta o trabalho pelo orçamento do frame.
    function buildModelGroup(desc, g, mats) {
        const meshes = g.parts.map((p) => {
            let mesh;
            if (p.shape === 'cylinder') { mesh = BABYLON.MeshBuilder.CreateCylinder(p.id, { diameterBottom: p.diameterBottom, diameterTop: p.diameterTop, height: p.height, tessellation: p.tessellation }, scene); }
            else if (p.shape === 'capsule') { mesh = BABYLON.MeshBuilder.CreateCapsule(p.id, { height: p.height, radius: (p.diameterBottom || p.diameter || 0) / 2, subdivisions: p.subdivisions || 4 }, scene); }
            else if (p.shape === 'icosphere') { mesh = BABYLON.MeshBuilder.CreateIcoSphere(p.id, { radius: p.diameter / 2, subdivisions: p.subdivisions }, scene); }
            else if (p.shape === 'sphere') { mesh = BABYLON.MeshBuilder.CreateSphere(p.id, { diameter: p.diameter, segments: p.tessellation }, scene); }
            else if (p.shape === 'plane') {
                // Plano vertical (vegetação com alfa); depth > width marca o plano cruzado a 90°.
                mesh = BABYLON.MeshBuilder.CreatePlane(p.id, { width: Math.max(p.width || 1, p.depth || 0), height: p.height }, scene);
                if (p.depth > p.width) { mesh.rotation.y = Math.PI / 2; mesh.bakeCurrentTransformIntoVertices(); }
            }
            else { mesh = BABYLON.MeshBuilder.CreateBox(p.id, { width: p.width || 1, height: p.height, depth: p.depth || 1 }, scene); }
            // Escala não uniforme (ex.: sapatos achatados): embutida nos vértices em
            // torno do centro, então a base efetiva desce scaleY·altura/2 abaixo dele.
            const sy = p.scaleY || 1;
            mesh.position.set(p.x, p.baseY + p.height / 2 * sy, p.z);   // descritor posiciona pela base
            if (p.scaleX || p.scaleY || p.scaleZ) {
                mesh.scaling.set(p.scaleX || 1, sy, p.scaleZ || 1);
                mesh.bakeCurrentTransformIntoVertices();
            }
            return mesh;
        });
        const merged = meshes.length === 1 ? meshes[0] : BABYLON.Mesh.MergeMeshes(meshes, true, true, undefined, false, true);
        if (meshes.length === 1) { merged.bakeCurrentTransformIntoVertices(); }
        merged.name = 'model-' + desc.id + '-' + g.id;
        for (const mod of g.modifiers) {
            if (mod.kind === 'jitter') { jitterMesh(merged, mod.amount, mod.seed); }
            else if (mod.kind === 'flute') { fluteMesh(merged, mod.amount, mod.seed); }
            else if (mod.kind === 'ao') { bakeVertexAO(merged, mod.amount, mod.seed); }
        }
        merged.material = mats[g.material];
        merged.isVisible = false;
        if (g.castShadow && sceneShadows) { sceneShadows.addShadowCaster(merged); }
        return merged;
    }

    const modelRegistry = new Map();
    const modelStatus = { loaded: [], placeholders: [], errors: [], log: [], queue: 0, timings: [] };
    // Entidades (jogador, Guardiã, remotos) que esperam um modelo chegar do carregador C#.
    const modelAwaiters = new Map();   // id do modelo -> [{ make, apply, fallback }]
    const characterSpawns = new Map(); // chave 'modelo#nome' -> { root, onModel, onFallback }

    // Instancia um modelo registrado como root único (TransformNode) com origem nos pés:
    // cada grupo vira clone visível filho do root. Se o modelo ainda não chegou, entra na
    // fila e usa o fallback JS imediatamente; quando o modelo chega, o fallback é
    // substituído. makeFallback deve devolver uma malha já posicionada (centro na origem).
    function spawnFromModel(modelId, name, groundPos, rotationY, makeFallback) {
        const root = new BABYLON.TransformNode(name, scene);
        root.position.copyFrom(groundPos);
        root.rotation.y = rotationY || 0;
        const entry = { root: root, onModel: null, onFallback: null };
        characterSpawns.set(modelId + '#' + name, entry);
        if (modelRegistry.has(modelId)) {
            attachCharacterModel(modelId, entry);
        } else {
            const fallback = makeFallback();   // malha centrada, y local já definido
            fallback.parent = root;
            entry.onFallback = fallback;
            if (sceneShadows) { sceneShadows.addShadowCaster(fallback); }
            const list = modelAwaiters.get(modelId) || [];
            list.push({ name: name, entry: entry, make: makeFallback });
            modelAwaiters.set(modelId, list);
        }
        return root;
    }

    // Troca o fallback JS pelas malhas do modelo C# (clones dos protótipos do descritor).
    function attachCharacterModel(modelId, entry) {
        const reg = modelRegistry.get(modelId);
        if (!reg || !entry.root) { return; }
        if (entry.onFallback) {
            if (sceneShadows) { sceneShadows.removeShadowCaster(entry.onFallback); }
            entry.onFallback.dispose(false, true);
            entry.onFallback = null;
        }
        for (const proto of reg.protos) {
            const clone = proto.clone(proto.name.replace('model-', 'char-') + '-' + entry.root.name);
            clone.isVisible = true;
            clone.parent = entry.root;
            clone.position.set(0, 0, 0);
            if (sceneShadows && reg.desc && !reg.placeholder) { sceneShadows.addShadowCaster(clone); }
        }
    }

    // ---------- Fila de entrada de modelos com orçamento por frame ----------
    // O registro de uma DLL (texturas procedurais, malhas, modificadores e espalhamento)
    // entra numa fila drenada pelo loop de render: no máximo MODEL_BUDGET_MS de CPU por
    // frame, sempre executando ao menos um passo (padrão do lineage-rust: entrada
    // amortizada, sem travar o frame). Um passo que falha derruba só o modelo dele.

    const modelQueue = [];

    // O que fazer no mundo quando cada modelo chega do carregador C#: geradores de passos
    // de espalhamento em lotes.
    const modelPlacements = {
        'tree.pine': function* (protos, desc) {
            yield* batchedSteps('árvores', makeTreePlacer(protos, desc.groundSink || 0, 64));
        },
        // Pedras: mesmas regras do protótipo antigo (26 por grupo, escala 0,6–2,2,
        // centro afundado 0,25 m no terreno).
        'rock.boulder': function* (protos) {
            for (const proto of protos) {
                yield* batchedSteps('pedras', makeInstancePlacer(proto, 26, { minScale: 0.6, maxScale: 2.2, maxAlt: 26, maxSlope: 0.9, sink: -0.25, minR: 6 }));
            }
        },
        // Tufos de grama: mesmas 420 posições/regras do protótipo antigo (vale, solo de grama).
        'scenery.grass': function* (protos) {
            yield* batchedSteps('grama', makeInstancePlacer(protos[0], 420, { minScale: 0.7, maxScale: 1.5, maxAlt: 14, maxSlope: 0.55, sink: 0, minR: 4.5 }));
        }
    };

    // Embrulha um colocador resumível em passos de lote para a fila de modelos.
    function* batchedSteps(label, placer) {
        let done = false;
        while (!done) {
            yield { label: label, run: function () { done = placer(MODEL_SCATTER_BATCH); } };
        }
    }

    // Trabalho de um modelo em passos: um material por passo, um grupo por passo,
    // registro no mundo, e o espalhamento em lotes.
    function* modelWork(desc, placeholder) {
        const mats = {};
        const protos = [];
        let registered = false;
        for (const m of desc.materials) {
            yield { label: desc.id + ':material ' + m.id, run: function () { mats[m.id] = modelMaterial(m); } };
        }
        for (const g of desc.groups) {
            yield { label: desc.id + ':grupo ' + g.id, run: function () { protos.push(buildModelGroup(desc, g, mats)); } };
        }
        yield {
            label: desc.id + ':registrar',
            run: function () {
                if (modelRegistry.has(desc.id)) { return; }   // placeholder perdeu a corrida para o modelo real
                modelRegistry.set(desc.id, { desc: desc, protos: protos, placeholder: !!placeholder });
                (placeholder ? modelStatus.placeholders : modelStatus.loaded).push(desc.id + ' v' + desc.version);
                // Personagens (jogador, Guardiã, remotos) trocam o fallback pelas malhas do modelo.
                const waiting = modelAwaiters.get(desc.id);
                if (waiting) {
                    for (const w of waiting) { attachCharacterModel(desc.id, w.entry); }
                    modelAwaiters.delete(desc.id);
                }
                registered = true;
            }
        };
        if (registered) {
            const place = modelPlacements[desc.id];
            if (place) { yield* place(protos, desc); }
        }
    }

    function enqueueModel(desc, placeholder) {
        modelQueue.push({
            timing: { id: desc.id, queuedAt: Math.round(performance.now()), cpuMs: 0, steps: 0, maxStepMs: 0 },
            work: modelWork(desc, placeholder)
        });
        modelStatus.queue = modelQueue.length;
    }

    // Drena a fila até o orçamento do frame; o primeiro passo do item da frente sempre roda.
    function drainModelQueue(frameStart) {
        while (modelQueue.length) {
            if (modelQueue[0].timing.steps > 0 && performance.now() - frameStart >= MODEL_BUDGET_MS) { break; }
            const item = modelQueue[0];
            let step;
            try { step = item.work.next(); }
            catch (e) { modelQueue.shift(); modelFailed(item, e); continue; }
            if (step.done) {
                modelQueue.shift();
                item.timing.doneAt = Math.round(performance.now());
                modelStatus.timings.push(item.timing);
                continue;
            }
            const s0 = performance.now();
            try { step.value.run(); }
            catch (e) { modelQueue.shift(); modelFailed(item, e); continue; }
            const stepMs = performance.now() - s0;
            item.timing.cpuMs = Math.round((item.timing.cpuMs + stepMs) * 100) / 100;
            item.timing.steps += 1;
            item.timing.maxStepMs = Math.round(Math.max(item.timing.maxStepMs, stepMs) * 100) / 100;
        }
        modelStatus.queue = modelQueue.length;
    }

    function modelFailed(item, e) {
        item.timing.error = String(e && e.message || e);
        item.timing.doneAt = Math.round(performance.now());
        modelStatus.errors.push(item.timing.id + ': ' + item.timing.error);
        modelStatus.timings.push(item.timing);
        modelStatus.queue = modelQueue.length;
        console.warn('[modelos] falha ao processar', item.timing.id, e);
    }

    // Monitor de custo de CPU do loop de render (evidência da carga sem travadas).
    const perfCpu = [];
    let perfCpuMax = 0;
    function framePerf(cpuMs) {
        perfCpu.push(Math.round(cpuMs * 100) / 100);
        if (perfCpu.length > 600) { perfCpu.shift(); }
        if (cpuMs > perfCpuMax) { perfCpuMax = cpuMs; }
    }

    // Espalha instâncias de um protótipo com filtros de altitude/inclinação (resumível,
    // em lotes, para o orçamento por frame da fila de modelos).
    function makeInstancePlacer(proto, count, opts) {
        let placed = 0, tries = 0;
        return function (n) {
            while (placed < count && tries < count * 40 && n > 0) {
                tries += 1;
                const x = (Math.random() * 2 - 1) * (WORLD_HALF + 4);
                const z = (Math.random() * 2 - 1) * (WORLD_HALF + 4);
                const r = Math.hypot(x, z);
                if (r < (opts.minR || 5)) { continue; }
                if (Math.hypot(x - NPC_POSITION.x, z - NPC_POSITION.z) < 4.5) { continue; }
                if (pathBlend(x, z) < 0.72) { continue; }
                const y = terrainHeight(x, z);
                if (opts.maxAlt !== undefined && y > opts.maxAlt) { continue; }
                if (slopeAt(x, z) > (opts.maxSlope || 0.6)) { continue; }
                const s = opts.minScale + Math.random() * ((opts.maxScale || opts.minScale) - opts.minScale);
                const inst = proto.createInstance(proto.name + '-i' + placed);
                inst.position.set(x, y + (opts.sink || 0) * s, z);
                inst.rotation.y = Math.random() * Math.PI * 2;
                inst.scaling.set(s, s * (0.85 + Math.random() * 0.5), s);
                placed += 1;
                n -= 1;
            }
            return placed >= count || tries >= count * 40;
        };
    }

    // ---------- Assets GLB CC0 (catálogo assets/assets.json assinado por SHA-256) ----------
    // Pipeline paralelo ao das DLLs de modelos: os GLBs curados (assets/glb, CC0 do
    // Quaternius — proveniência em assets/LICENSE.md) são dados, não código. O download
    // confere o SHA-256 do catálogo antes de usar (bytes divergentes são recusados), os
    // bytes validados vão ao cache OPFS do game.assets (chave endereçada por conteúdo)
    // e o espalhamento roda na fila de modelos, respeitando o orçamento por frame.
    // game.glb.register(id, bytes) é a costura para produtores futuros (IA interna/API).
    const GLB_LOADERS = {
        src: 'js/vendor/babylonjs.loaders-9.36.0.min.js',   // vendorizado e pinado (assets/LICENSE.md)
        integrity: 'sha384-OXd9U4rMASknPaxNfNxZEGFw9NDqAW2RISOw9OBdTcMureZGhfxRzaKspEgn8D+L'
    };
    const glbStatus = { loaded: [], errors: [], queued: 0, loaders: 'pending' };
    const glbRegistry = new Map();   // id -> { proto, entry }
    let glbLoadersPromise = null;

    // Loader glTF oficial injetado sob demanda: o primeiro asset GLB paga o custo, o
    // primeiro paint do jogo não muda. SRI fecha o arquivo vendorizado no hash do git.
    function ensureGlbLoaders() {
        if (BABYLON.GLTF2 || BABYLON.GLTFFileLoader) { glbStatus.loaders = 'ready'; return Promise.resolve(); }
        if (!glbLoadersPromise) { glbLoadersPromise = dom ? ensureGlbLoadersScript() : ensureGlbLoadersVerified(); }
        return glbLoadersPromise;
    }

    // Main thread: <script> com SRI (o navegador confere o SHA-384 do arquivo vendorizado).
    function ensureGlbLoadersScript() {
        return new Promise(function (resolve, reject) {
            const s = document.createElement('script');
            s.src = GLB_LOADERS.src;
            s.integrity = GLB_LOADERS.integrity;
            s.crossOrigin = 'anonymous';
            s.onload = function () { glbStatus.loaders = 'ready'; resolve(); };
            s.onerror = function () {
                glbStatus.loaders = 'failed';
                glbLoadersPromise = null;
                reject(new Error('loader glTF não carregou: ' + GLB_LOADERS.src));
            };
            document.head.appendChild(s);
        });
    }

    // Worker não tem <script>: baixa o loader, confere o MESMO SHA-384 do SRI via
    // crypto.subtle e importa por blob URL — a garantia de integridade se mantém.
    async function ensureGlbLoadersVerified() {
        try {
            const res = await fetch(pageUrl(GLB_LOADERS.src));
            if (!res.ok) { throw new Error('HTTP ' + res.status); }
            const bytes = await res.arrayBuffer();
            const digest = new Uint8Array(await crypto.subtle.digest('SHA-384', bytes));
            let b64 = '';
            for (let i = 0; i < digest.length; i++) { b64 += String.fromCharCode(digest[i]); }
            if ('sha384-' + btoa(b64) !== GLB_LOADERS.integrity) { throw new Error('SHA-384 do loader glTF divergente'); }
            importScripts(URL.createObjectURL(new Blob([bytes], { type: 'text/javascript' })));
            glbStatus.loaders = 'ready';
        } catch (e) {
            glbStatus.loaders = 'failed';
            glbLoadersPromise = null;
            throw e;
        }
    }

    async function sha256Hex(buf) {
        const digest = await crypto.subtle.digest('SHA-256', buf);
        return Array.from(new Uint8Array(digest), function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    // Chave de cache endereçada por conteúdo (id+sha do arquivo): um asset igual
    // continua válido entre builds, diferente das texturas (chave inclui o build).
    async function glbCacheKey(entry) {
        const data = new TextEncoder().encode(JSON.stringify(['glb-v1', entry.id, entry.sha256]));
        return sha256Hex(data);
    }

    // Download verificado (com cache OPFS dos bytes já validados). Falha de rede ou de
    // hash derruba só este asset — o mundo segue com os modelos DLL e os demais GLBs.
    async function fetchVerifiedGlb(entry) {
        const hasCache = !!(G.game && G.game.assets && G.game.assets.report().opfs);
        const key = hasCache ? await glbCacheKey(entry) : null;
        if (key) {
            const hit = await G.game.assets.load(key);
            if (hit && hit.parts[0] && hit.parts[0].byteLength > 0) { return hit.parts[0]; }
        }
        const res = await fetch(pageUrl(entry.file) + '?h=' + entry.sha256);
        if (!res.ok) { throw new Error('HTTP ' + res.status + ' em ' + entry.file); }
        const buf = await res.arrayBuffer();
        const sha = await sha256Hex(buf);
        if (sha !== entry.sha256) { throw new Error('SHA-256 divergente (' + sha.slice(0, 12) + '… ≠ catálogo)'); }
        if (key) {
            G.game.assets.save(key, { v: 1, kind: 'glb', id: entry.id, sha256: entry.sha256, parts: [buf.byteLength] }, [buf])
                .catch(function () { /* cache é só otimização */ });
        }
        return buf;
    }

    // glTF traz materiais PBR; o resto do jogo usa StandardMaterial com a iluminação
    // hemisférica+sol calibrada para ele. Converte cor/textura albedo — a aparência fica
    // coerente com árvores/pedras/grama e mais leve de renderizar que PBR por instância.
    function glbMaterialToStandard(mat) {
        if (!mat || !BABYLON.PBRMaterial || !(mat instanceof BABYLON.PBRMaterial)) { return mat; }
        const std = new BABYLON.StandardMaterial(mat.name + '-std', scene);
        std.diffuseColor = mat.albedoColor ? mat.albedoColor.clone() : new BABYLON.Color3(0.8, 0.8, 0.8);
        if (mat.albedoTexture) {
            std.diffuseTexture = mat.albedoTexture;
            if (mat.albedoTexture.hasAlpha) { std.useAlphaFromDiffuseTexture = true; }
        }
        std.emissiveColor = mat.emissiveColor ? mat.emissiveColor.clone() : new BABYLON.Color3(0, 0, 0);
        std.specularColor = new BABYLON.Color3(0.06, 0.06, 0.06);
        if (typeof mat.alpha === 'number' && mat.alpha < 1) { std.alpha = mat.alpha; }
        return std;
    }

    // Importa os bytes pelo SceneLoader com URL de Blob + extensão explícita do plugin
    // glTF ('.glb'): o core decodifica o binário e alimenta o plugin — sem arquivo
    // temporário e sem segunda ida à rede. Devolve o protótipo invisível com origem no
    // chão (mesma convenção dos descritores DLL) e a maior dimensão = scaleM metros.
    async function importGlbProto(id, bytes, scaleM) {
        await ensureGlbLoaders();
        const url = URL.createObjectURL(new Blob([bytes], { type: 'model/gltf-binary' }));
        let result;
        try {
            result = await BABYLON.SceneLoader.ImportMeshAsync('', url, '', scene, undefined, '.glb');
        } finally {
            URL.revokeObjectURL(url);
        }
        for (const g of result.animationGroups || []) { g.stop(); }   // cenário é estático
        const meshes = (result.meshes || []).filter(function (m) {
            return m && m instanceof BABYLON.Mesh && !m.isAnInstance && m.getTotalVertices() > 0;
        });
        if (!meshes.length) { throw new Error('nenhuma malha utilizável no GLB'); }
        for (const m of meshes) {
            m.setParent(null);   // preserva a transformada mundial nos valores locais
            m.material = glbMaterialToStandard(m.material);
        }
        const merged = meshes.length === 1 ? meshes[0] : BABYLON.Mesh.MergeMeshes(meshes, true, true, undefined, false, true);
        if (!merged) { throw new Error('falha ao mesclar malhas'); }
        // Normaliza: escala uniforme até a maior dimensão = scaleM metros e origem no
        // centro da base (min.y = 0) — o colocador só posiciona/rotaciona/escala.
        merged.refreshBoundingInfo();
        const min = merged.getBoundingInfo().boundingBox.minimumWorld;
        const max = merged.getBoundingInfo().boundingBox.maximumWorld;
        const maxDim = Math.max(max.x - min.x, max.y - min.y, max.z - min.z) || 1;
        const k = scaleM / maxDim;
        merged.scaling.setAll(k);
        merged.position.set(-(min.x + max.x) / 2 * k, -min.y * k, -(min.z + max.z) / 2 * k);
        merged.bakeCurrentTransformIntoVertices();
        merged.refreshBoundingInfo();
        merged.name = 'glb-' + id;
        merged.isVisible = false;
        merged.isPickable = false;
        return merged;
    }

    // Como cada asset entra no mundo: escala canônica (scaleM), sombra e espalhamento
    // reusando os colocadores resumíveis (mesmos filtros de trilha/altitude/inclinação).
    const glbPlacements = {
        'scenery.bush': { scaleM: 0.95, shadows: true, place: (p) => batchedSteps('arbustos', makeInstancePlacer(p, 120, { minScale: 0.7, maxScale: 1.5, maxAlt: 15, maxSlope: 0.55, sink: 0.02, minR: 5 })) },
        'flora.flowers': { scaleM: 0.55, shadows: false, place: (p) => batchedSteps('flores', makeInstancePlacer(p, 160, { minScale: 0.7, maxScale: 1.3, maxAlt: 12, maxSlope: 0.5, sink: 0.01, minR: 4.5 })) },
        'flora.mushroom': { scaleM: 0.32, shadows: false, place: (p) => batchedSteps('cogumelos', makeInstancePlacer(p, 60, { minScale: 0.7, maxScale: 1.6, maxAlt: 12, maxSlope: 0.5, sink: 0.01, minR: 5 })) },
        'flora.fern': { scaleM: 1.15, shadows: false, place: (p) => batchedSteps('samambaias', makeInstancePlacer(p, 80, { minScale: 0.7, maxScale: 1.4, maxAlt: 14, maxSlope: 0.55, sink: 0.02, minR: 5 })) },
        'prop.log': { scaleM: 2.3, shadows: true, place: (p) => batchedSteps('troncos', makeInstancePlacer(p, 14, { minScale: 0.8, maxScale: 1.3, maxAlt: 15, maxSlope: 0.4, sink: 0.06, minR: 6 })) },
        'prop.stump': { scaleM: 0.75, shadows: true, place: (p) => batchedSteps('tocos', makeInstancePlacer(p, 16, { minScale: 0.8, maxScale: 1.4, maxAlt: 16, maxSlope: 0.45, sink: 0.04, minR: 6 })) }
    };

    // Um asset do catálogo: rede/hash fora da fila (I/O), o espalhamento (CPU real:
    // terreno/ruído por instância) entra na fila de modelos com o orçamento por frame.
    async function loadGlbAsset(entry) {
        const cfg = glbPlacements[entry.id];
        const bytes = await fetchVerifiedGlb(entry);
        const proto = await importGlbProto(entry.id, bytes, cfg ? cfg.scaleM : 1);
        glbRegistry.set(entry.id, { proto: proto, entry: entry });
        glbStatus.loaded.push(entry.id);
        if (cfg) {
            if (cfg.shadows && sceneShadows) { sceneShadows.addShadowCaster(proto); }
            modelQueue.push({
                timing: { id: entry.id, queuedAt: Math.round(performance.now()), cpuMs: 0, steps: 0, maxStepMs: 0 },
                work: cfg.place(proto)
            });
            modelStatus.queue = modelQueue.length;
        }
    }

    async function glbLoadAll() {
        try {
            const res = await fetch(pageUrl('assets/assets.json?v=' + Date.now()));
            if (!res.ok) { throw new Error('catálogo assets.json: HTTP ' + res.status); }
            const cat = await res.json();
            glbStatus.queued = (cat.assets || []).length;
            for (const entry of cat.assets || []) {
                loadGlbAsset(entry).catch(function (e) {
                    glbStatus.errors.push(entry.id + ': ' + (e && e.message || e));
                    console.warn('[glb] asset recusado, sigo sem ele:', entry.id, e);
                });
            }
        } catch (e) {
            glbStatus.errors.push('catálogo: ' + (e && e.message || e));
            console.warn('[glb] catálogo indisponível — sigo só com os modelos DLL:', e);
        }
    }

    function buildScene(canvas) {
        scene = new BABYLON.Scene(engine);
        const horizon = new BABYLON.Color3(0.72, 0.80, 0.88);
        scene.clearColor = new BABYLON.Color4(horizon.r, horizon.g, horizon.b, 1);
        scene.fogMode = BABYLON.Scene.FOGMODE_EXP2;
        scene.fogColor = horizon;
        scene.fogDensity = 0.0105;
        scene.ambientColor = new BABYLON.Color3(0.26, 0.30, 0.34);

        // Tone mapping ACES: realça luz e sombra sem estourar os claros.
        const proc = scene.imageProcessingConfiguration;
        proc.toneMappingEnabled = true;
        proc.toneMappingType = BABYLON.ImageProcessingConfiguration.TONEMAPPING_ACES;
        proc.exposure = 1.25;
        proc.contrast = 1.1;
        proc.vignetteEnabled = true;
        proc.vignetteWeight = 0.8;

        const hemi = new BABYLON.HemisphericLight('hemi', new BABYLON.Vector3(0.2, 1, 0.1), scene);
        hemi.intensity = 0.85;
        hemi.diffuse = new BABYLON.Color3(0.65, 0.72, 0.85);      // luz fria do céu
        hemi.groundColor = new BABYLON.Color3(0.38, 0.42, 0.30);  // ricochete do chão

        const sun = new BABYLON.DirectionalLight('sun', new BABYLON.Vector3(-0.7, -0.8, -0.3).normalize(), scene);
        sun.position = new BABYLON.Vector3(60, 70, 25);
        sun.intensity = 1.6;
        sun.diffuse = new BABYLON.Color3(1.0, 0.95, 0.85);        // sol da tarde
        sun.specular = new BABYLON.Color3(1.0, 0.97, 0.9);

        const shadows = new BABYLON.ShadowGenerator(2048, sun);
        shadows.usePercentageCloserFiltering = true;       // sombra de bordas suaves (WebGL2)
        shadows.filteringQuality = BABYLON.ShadowGenerator.QUALITY_MEDIUM;
        shadows.bias = 0.0006;
        shadows.normalBias = 0.02;
        shadows.darkness = 0.3;

        skyDome();
        const ground = buildTerrain();

        // Árvores: chegam como modelos C# carregados em runtime (game.models.register).
        sceneShadows = shadows;

        // Pedras: chegam como modelos C# carregados em runtime (game.models.register).

        // Tufos de grama no vale: chegam como modelo C# (scenery.grass) carregado em runtime.

        // Jogador: humanoide de 1,75 m — modelo C# (character.villager) carregado em runtime;
        // enquanto a DLL não chega, o protótipo JS serve de fallback. Origem do root = pés.
        player = spawnFromModel('character.villager', 'player',
            new BABYLON.Vector3(0, terrainHeight(0, 0) + 0.03, 0), 0,
            () => { const m = buildHumanoid('player-js', { shirt:new BABYLON.Color3(0.20,0.50,0.92), pants:new BABYLON.Color3(0.20,0.22,0.28), skin:new BABYLON.Color3(0.90,0.72,0.58) }); m.position.y = PLAYER_HEIGHT / 2; return m; });

        // Guardiã: figura encapuzada dourada (1,7 m) com anel de destaque no chão —
        // modelo C# (character.guardian) carregado em runtime, fallback JS centrado.
        const npc = spawnFromModel('character.guardian', 'npc',
            new BABYLON.Vector3(NPC_POSITION.x, terrainHeight(NPC_POSITION.x, NPC_POSITION.z) + 0.03, NPC_POSITION.z), 0,
            () => { const m = buildGuardian('npc-js'); m.position.y = 0.85; return m; });

        // Andarilho: segundo NPC (character.wanderer) na clareira inicial, circulando
        // devagar — o cérebro dele (WandererBrain) vive em C#; aqui é só o corpo.
        wandererRoot = spawnFromModel('character.wanderer', 'wanderer',
            new BABYLON.Vector3(WANDERER_HOME.x, terrainHeight(WANDERER_HOME.x, WANDERER_HOME.z) + 0.03, WANDERER_HOME.z), 0,
            () => { const m = buildHumanoid('wanderer-js', { shirt:new BABYLON.Color3(0.26,0.58,0.44), pants:new BABYLON.Color3(0.34,0.30,0.24), skin:new BABYLON.Color3(0.86,0.67,0.50) }); m.position.y = PLAYER_HEIGHT / 2; return m; });
        let wanderT = Math.random() * Math.PI * 2;
        scene.registerBeforeRender(() => {
            if (!wandererRoot || !engine) { return; }
            wanderT += Math.min(0.05, engine.getDeltaTime() / 1000) * 0.22;
            wandererRoot.position.x = WANDERER_HOME.x + Math.sin(wanderT) * 1.4;
            wandererRoot.position.z = WANDERER_HOME.z + Math.cos(wanderT) * 1.4;
            wandererRoot.position.y = terrainHeight(wandererRoot.position.x, wandererRoot.position.z) + 0.03;
            wandererRoot.rotation.y = wanderT + Math.PI / 2;   // de frente para o caminhar
        });

        const marker = BABYLON.MeshBuilder.CreateTorus('npcMarker', { diameter: 1.5, thickness: 0.08, tessellation: 32 }, scene);
        marker.position = new BABYLON.Vector3(NPC_POSITION.x, terrainHeight(NPC_POSITION.x, NPC_POSITION.z) + 0.06, NPC_POSITION.z);
        const markerMat = new BABYLON.StandardMaterial('markerMat', scene);
        markerMat.emissiveColor = new BABYLON.Color3(0.95, 0.75, 0.2);
        marker.material = markerMat;
        scene.registerBeforeRender(() => { marker.rotation.y += 0.02; });

        camera = new BABYLON.ArcRotateCamera('camera', -Math.PI / 2, 1.12, 7.5, player.position.add(CAM_UP), scene);
        camera.lowerRadiusLimit = 2.4;
        camera.upperRadiusLimit = 40;
        camera.upperBetaLimit = Math.PI / 2.08;
        camera.wheelDeltaPercentage = 0.01;
        camera.fov = 0.85;
        if (dom) { camera.attachControl(canvas, true); }   // worker: input chega por api.input.camera

        // v3: pós-processamento (MSAA 4x, bloom discreto, nitidez) com recuo gracioso.
        try {
            const pipeline = new BABYLON.DefaultRenderingPipeline('pipe', true, scene, [camera]);
            pipeline.samples = 4;
            pipeline.fxaaEnabled = false;
            pipeline.bloomEnabled = true;
            pipeline.bloomThreshold = 0.85; pipeline.bloomWeight = 0.22; pipeline.bloomKernel = 48; pipeline.bloomScale = 0.5;
            pipeline.sharpenEnabled = true; pipeline.sharpen.edgeAmount = 0.22;
        } catch (e) { console.warn('Pós-processamento indisponível:', e); }

        // v3: poeira/pólen em suspensão ao redor do jogador (textura de disco suave desenhada em canvas).
        try {
            const dot = new BABYLON.DynamicTexture('moteTex', { width: 64, height: 64 }, scene, false);
            const dctx = dot.getContext(), grad = dctx.createRadialGradient(32, 32, 0, 32, 32, 32);
            grad.addColorStop(0, 'rgba(255,248,210,1)'); grad.addColorStop(0.4, 'rgba(255,240,180,0.45)'); grad.addColorStop(1, 'rgba(255,240,180,0)');
            dctx.fillStyle = grad; dctx.fillRect(0, 0, 64, 64); dot.update(); dot.hasAlpha = true;
            const motes = new BABYLON.ParticleSystem('motes', 260, scene);
            motes.particleTexture = dot;
            motes.emitter = player.position.clone();
            motes.minEmitBox = new BABYLON.Vector3(-22, 0.2, -22); motes.maxEmitBox = new BABYLON.Vector3(22, 6, 22);
            motes.color1 = new BABYLON.Color4(1, 0.95, 0.7, 0.55); motes.color2 = new BABYLON.Color4(0.9, 1, 0.8, 0.35);
            motes.colorDead = new BABYLON.Color4(1, 1, 0.8, 0);
            motes.minSize = 0.05; motes.maxSize = 0.14; motes.minLifeTime = 6; motes.maxLifeTime = 11; motes.emitRate = 34;
            motes.direction1 = new BABYLON.Vector3(-0.15, 0.06, -0.15); motes.direction2 = new BABYLON.Vector3(0.15, 0.2, 0.15);
            motes.minEmitPower = 0.2; motes.maxEmitPower = 0.5; motes.gravity = new BABYLON.Vector3(0, 0, 0);
            motes.blendMode = BABYLON.ParticleSystem.BLENDMODE_ADD;
            motes.start();
            scene.registerBeforeRender(() => { if (player) { motes.emitter.copyFrom(player.position); } });
        } catch (e) { console.warn('Partículas indisponíveis:', e); }
    }

    // Deadzone radial (skill input-systems): preserva direção diagonal e reescala a magnitude restante.
    function radialDeadzone(x, y) {
        const m = Math.hypot(x, y);
        if (m < DEADZONE) { return { x: 0, y: 0 }; }
        const scaled = Math.min(1, (m - DEADZONE) / (1 - DEADZONE));
        return { x: (x / m) * scaled, y: (y / m) * scaled };
    }

    // Dispositivo ativo mais recente: a dica de controles na tela acompanha (body[data-input]).
    let activeInput = 'keyboard';
    function setActiveInput(kind) {
        if (kind === activeInput) { return; }
        activeInput = kind;
        if (host.onInputMode) { host.onInputMode(kind); }
    }
    let cancelWasDown = false;

    function pollGamepad() {
        let x = 0, y = 0, lookX = 0, lookY = 0, action = false, cancel = false;
        try {
            const pads = navigator.getGamepads ? navigator.getGamepads() : [];
            const pad = Array.from(pads || []).find((p) => p && p.connected);
            if (pad) {
                const l = radialDeadzone(pad.axes[0] || 0, pad.axes[1] || 0);
                const r = radialDeadzone(pad.axes[2] || 0, pad.axes[3] || 0);
                x = l.x; y = -l.y; lookX = r.x; lookY = r.y;
                const down = (i) => !!(pad.buttons[i] && pad.buttons[i].pressed);
                action = down(0);                 // A / Cruz
                cancel = down(1) || down(9);      // B / Círculo ou Start
                // D-pad como alternativa ao stick esquerdo.
                if (down(14)) { x -= 1; } if (down(15)) { x += 1; }
                if (down(12)) { y += 1; } if (down(13)) { y -= 1; }
                if (x || y || lookX || lookY || action || cancel) { setActiveInput('gamepad'); }
            }
        } catch (e) { /* Gamepad API indisponível */ }
        if (action && !actionWasDown) { interact(); }
        if (cancel && !cancelWasDown && dotnet) { dotnet.invokeMethodAsync('OnClose'); }
        actionWasDown = action;
        cancelWasDown = cancel;
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
        const run = keys.ShiftLeft || keys.ShiftRight || magnitude > 0.92;
        const speed = run ? RUN_SPEED : WALK_SPEED;
        player.position.x += (dx / len) * speed * magnitude * deltaSeconds;
        player.position.z += (dz / len) * speed * magnitude * deltaSeconds;
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
        // Andarilho acompanha a posição viva do root (ele circula pela clareira).
        const wx = wandererRoot ? wandererRoot.position.x : WANDERER_HOME.x;
        const wz = wandererRoot ? wandererRoot.position.z : WANDERER_HOME.z;
        const nowWanderer = Math.hypot(player.position.x - wx, player.position.z - wz) < NEAR_RADIUS;
        if (nowWanderer !== nearWanderer) {
            nearWanderer = nowWanderer;
            if (dotnet) { dotnet.invokeMethodAsync('OnWandererNear', nowWanderer); }
        }
    }

    // Humanoide de escala métrica: pernas, tronco, braços e cabeça em cores distintas.
    function buildHumanoid(name, colors) {
        const H=colors.height||PLAYER_HEIGHT, legH=H*0.44, torsoH=H*0.38, armH=H*0.36, headR=H*0.07;
        const legR=H*0.040, torsoR=H*0.088, armR=H*0.026, feet=-H/2;
        const mat=(label,c)=>{const m=new BABYLON.StandardMaterial(name+'-'+label,scene);m.diffuseColor=c;m.specularColor=new BABYLON.Color3(0.08,0.08,0.08);return m;};
        const pants=mat('pants',colors.pants), shirt=mat('shirt',colors.shirt), skin=mat('skin',colors.skin), parts=[];
        for(const sx of [-1,1]){
            const leg=BABYLON.MeshBuilder.CreateCapsule(name+'-leg',{height:legH,radius:legR,subdivisions:4},scene);
            leg.position.set(sx*torsoR*0.5,feet+legH/2,0);leg.material=pants;parts.push(leg);
            const arm=BABYLON.MeshBuilder.CreateCapsule(name+'-arm',{height:armH,radius:armR,subdivisions:2},scene);
            arm.position.set(sx*(torsoR+armR*1.3),feet+legH+torsoH-H*0.20,0);arm.material=shirt;parts.push(arm);
        }
        const torso=BABYLON.MeshBuilder.CreateCapsule(name+'-torso',{height:torsoH,radius:torsoR,subdivisions:5},scene);
        torso.position.y=feet+legH+torsoH/2;torso.material=shirt;parts.push(torso);
        const head=BABYLON.MeshBuilder.CreateSphere(name+'-head',{diameter:headR*2,segments:8},scene);
        head.position.y=H/2-headR;head.material=skin;parts.push(head);
        // v3: sapatos, mãos, cabelo e olhos para leitura de silhueta e rosto.
        const shoeMat=mat('shoe',new BABYLON.Color3(0.13,0.10,0.09)), hairMat=mat('hair',colors.hair||new BABYLON.Color3(0.20,0.13,0.08)), eyeMat=mat('eye',new BABYLON.Color3(0.05,0.05,0.06));
        for(const sx of [-1,1]){
            const shoe=BABYLON.MeshBuilder.CreateSphere(name+'-shoe',{diameter:legR*2.5,segments:8},scene);
            shoe.scaling.set(1,0.55,1.9);shoe.position.set(sx*torsoR*0.5,feet+legR*0.55,legR*0.9);shoe.material=shoeMat;parts.push(shoe);
            const hand=BABYLON.MeshBuilder.CreateSphere(name+'-hand',{diameter:armR*2.6,segments:8},scene);
            hand.position.set(sx*(torsoR+armR*1.3),feet+legH+torsoH-H*0.20-armH/2,0);hand.material=skin;parts.push(hand);
            const eye=BABYLON.MeshBuilder.CreateSphere(name+'-eye',{diameter:headR*0.22,segments:6},scene);
            eye.position.set(sx*headR*0.36,H/2-headR+headR*0.1,headR*0.9);eye.material=eyeMat;parts.push(eye);
        }
        const neck=BABYLON.MeshBuilder.CreateCylinder(name+'-neck',{diameter:headR*0.9,height:headR*0.7,tessellation:10},scene);
        neck.position.y=H/2-headR*2+headR*0.1;neck.material=skin;parts.push(neck);
        const hair=BABYLON.MeshBuilder.CreateSphere(name+'-hair',{diameter:headR*2.18,segments:10},scene);
        hair.scaling.set(1,0.82,1.02);hair.position.set(0,H/2-headR+headR*0.28,-headR*0.14);hair.material=hairMat;parts.push(hair);
        const body=BABYLON.Mesh.MergeMeshes(parts,true,true,undefined,true,true);body.name=name;return body;
    }

    // Guardiã com manto, túnica, rosto e capuz em escala de 1,7 m.
    function buildGuardian(name) {
        const H=1.7, robeH=H*0.52, torsoH=H*0.30, headR=H*0.075, feet=-H/2;
        const mat=(label,c,e)=>{const m=new BABYLON.StandardMaterial(name+'-'+label,scene);m.diffuseColor=c;m.emissiveColor=e||new BABYLON.Color3(0,0,0);return m;};
        const gold=mat('gold',new BABYLON.Color3(0.85,0.64,0.20),new BABYLON.Color3(0.10,0.06,0.01));
        const dark=mat('dark',new BABYLON.Color3(0.66,0.48,0.14),new BABYLON.Color3(0.08,0.05,0.01));
        const skin=mat('skin',new BABYLON.Color3(0.92,0.76,0.60)), hood=mat('hood',new BABYLON.Color3(0.52,0.36,0.10),new BABYLON.Color3(0.06,0.035,0.008));
        const parts=[];
        const robe=BABYLON.MeshBuilder.CreateCylinder(name+'-robe',{diameterTop:0.30,diameterBottom:0.66,height:robeH,tessellation:12},scene);
        robe.position.y=feet+robeH/2;robe.material=gold;parts.push(robe);
        const torso=BABYLON.MeshBuilder.CreateCapsule(name+'-torso',{height:torsoH,radius:H*0.085,subdivisions:3},scene);
        torso.position.y=feet+robeH+torsoH/2;torso.material=dark;parts.push(torso);
        const head=BABYLON.MeshBuilder.CreateSphere(name+'-head',{diameter:headR*2,segments:8},scene);
        head.position.y=H/2-headR;head.material=skin;parts.push(head);
        const cap=BABYLON.MeshBuilder.CreateCylinder(name+'-hood',{diameterTop:0,diameterBottom:0.34,height:0.30,tessellation:10},scene);
        cap.position.y=H/2+0.09;cap.material=hood;parts.push(cap);
        const body=BABYLON.Mesh.MergeMeshes(parts,true,true,undefined,true,true);body.name=name;return body;
    }

    function drawRemoteLabel(texture, name) {
        texture.clear();
        texture.drawText(String(name || 'Jogador').slice(0, 28), 12, 70, 'bold 42px sans-serif', '#ffffff', 'transparent', true);
    }

    function addRemote(id, name) {
        if (remotes.has(id) || !scene) { return; }
        const serial = remoteSerial++;
        const avatar = spawnFromModel('character.wanderer', 'remote-' + id,
            new BABYLON.Vector3((serial % 5 - 2) * 1.5, terrainHeight(0, -2) + 0.03, -2 - Math.floor(serial / 5) * 2), 0,
            () => { const m = buildHumanoid('remote-js-' + id, { shirt:new BABYLON.Color3(0.66,0.30,0.82), pants:new BABYLON.Color3(0.24,0.18,0.30), skin:new BABYLON.Color3(0.88,0.70,0.56) }); m.position.y = PLAYER_HEIGHT / 2; return m; });

        const label = BABYLON.MeshBuilder.CreatePlane('remote-label-' + id, { width: 1.8, height: 0.36 }, scene);
        label.position = avatar.position.add(new BABYLON.Vector3(0, 1.925, 0));
        label.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;
        const texture = new BABYLON.DynamicTexture('remote-name-' + id, { width: 512, height: 100 }, scene, false);
        texture.hasAlpha = true;
        drawRemoteLabel(texture, name);
        const labelMat = new BABYLON.StandardMaterial('remote-label-mat-' + id, scene);
        labelMat.diffuseTexture = texture;
        labelMat.emissiveColor = new BABYLON.Color3(1, 1, 1);
        labelMat.opacityTexture = texture;
        label.material = labelMat;
        remotes.set(id, { avatar: avatar, label: label, texture: texture, labelMat: labelMat });
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
        p.avatar.position.y = terrainHeight(p.avatar.position.x, p.avatar.position.z) + 0.03;
        p.avatar.rotation.y = ry;
        p.label.position.x = p.avatar.position.x;
        p.label.position.z = p.avatar.position.z;
        p.label.position.y = p.avatar.position.y + 1.925;
    }

    function removeRemote(id) {
        const p = remotes.get(id);
        if (!p) { return; }
        p.avatar.dispose(false, true); p.label.dispose(); p.texture.dispose(); p.labelMat.dispose();
        remotes.delete(id);
        if (dotnet) { dotnet.invokeMethodAsync('OnRemoteCount', remotes.size); }
    }

    // --- Mapa editável persistido (servidor autoritativo): tiles + jobs ---
    const MAP_CELL = 4.0; // deve espelhar CELL do backend (app/main.py)
    const mapTileMeshes = new Map(); // "cx,cz" -> mesh
    const mapJobMeshes = new Map();  // job.id -> { ring, label, texture, mat }

    function cellWorld(cx, cz) { return { x: cx * MAP_CELL, z: cz * MAP_CELL }; }

    const MAP_TILE_STYLES = {
        pit: { color: [0.22, 0.15, 0.10], alpha: 0.92 },   // terra escavada
        path: { color: [0.78, 0.68, 0.48], alpha: 0.90 },  // caminho de terra batida
        water: { color: [0.16, 0.42, 0.72], alpha: 0.80 }, // água
        rock: { color: [0.45, 0.45, 0.48], alpha: 1.0 }    // rocha
    };

    function setMapTiles(tiles) {
        if (!scene) { return; }
        const seen = new Set();
        for (const key of Object.keys(tiles || {})) {
            const t = tiles[key] || {};
            seen.add(key);
            const type = MAP_TILE_STYLES[t.type] ? t.type : null;
            if (!type) { continue; }
            const existing = mapTileMeshes.get(key);
            if (existing && existing.metadata && existing.metadata.type === type) { continue; }
            if (existing) { existing.dispose(); mapTileMeshes.delete(key); }
            const parts = String(key).split(',');
            const cx = Number(parts[0]), cz = Number(parts[1]);
            if (!Number.isFinite(cx) || !Number.isFinite(cz)) { continue; }
            const w = cellWorld(cx, cz);
            const style = MAP_TILE_STYLES[type];
            const disc = BABYLON.MeshBuilder.CreateDisc('tile-' + type + '-' + key, { radius: 1.9, tessellation: 24 }, scene);
            disc.rotation.x = Math.PI / 2;
            disc.position.set(w.x, terrainHeight(w.x, w.z) + 0.05, w.z);
            const mat = new BABYLON.StandardMaterial('tile-' + type + '-mat-' + key, scene);
            mat.diffuseColor = new BABYLON.Color3(style.color[0], style.color[1], style.color[2]);
            mat.specularColor = new BABYLON.Color3(0, 0, 0);
            mat.alpha = style.alpha;
            disc.material = mat;
            disc.metadata = { type: type };
            mapTileMeshes.set(key, disc);
        }
        for (const key of Array.from(mapTileMeshes.keys())) {
            if (!seen.has(key)) {
                mapTileMeshes.get(key).dispose();
                mapTileMeshes.delete(key);
            }
        }
    }

    function setMapJobs(jobs) {
        if (!scene) { return; }
        const seen = new Set();
        for (const job of (jobs || [])) {
            if (!job || !job.id) { continue; }
            seen.add(job.id);
            const w = cellWorld(Number(job.cx), Number(job.cz));
            const y = terrainHeight(w.x, w.z);
            let entry = mapJobMeshes.get(job.id);
            if (!entry) {
                const ring = BABYLON.MeshBuilder.CreateTorus('job-' + job.id, { diameter: 3.2, thickness: 0.2, tessellation: 24 }, scene);
                ring.position.set(w.x, y + 0.3, w.z);
                const rmat = new BABYLON.StandardMaterial('job-mat-' + job.id, scene);
                rmat.emissiveColor = new BABYLON.Color3(0.95, 0.62, 0.12);
                rmat.diffuseColor = new BABYLON.Color3(0, 0, 0);
                ring.material = rmat;
                const label = BABYLON.MeshBuilder.CreatePlane('job-label-' + job.id, { width: 2.6, height: 0.55 }, scene);
                label.position.set(w.x, y + 1.7, w.z);
                label.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;
                const texture = new BABYLON.DynamicTexture('job-tex-' + job.id, { width: 512, height: 110 }, scene, false);
                texture.hasAlpha = true;
                const lmat = new BABYLON.StandardMaterial('job-lmat-' + job.id, scene);
                lmat.diffuseTexture = texture;
                lmat.opacityTexture = texture;
                lmat.emissiveColor = new BABYLON.Color3(1, 1, 1);
                label.material = lmat;
                entry = { ring: ring, label: label, texture: texture, mat: lmat };
                mapJobMeshes.set(job.id, entry);
            }
            const pct = Math.round(Math.max(0, Math.min(1, Number(job.progress) || 0)) * 100);
            entry.texture.clear();
            entry.texture.drawText((job.ownerName || '?') + ' — ' + pct + '%', 10, 74,
                'bold 44px sans-serif', '#ffd27d', 'transparent', true, true);
        }
        for (const id of Array.from(mapJobMeshes.keys())) {
            if (!seen.has(id)) {
                const entry = mapJobMeshes.get(id);
                entry.ring.dispose(); entry.label.dispose(); entry.texture.dispose(); entry.mat.dispose();
                mapJobMeshes.delete(id);
            }
        }
    }

    function tick(nowMs) {
        if (nowMs - lastTick < TICK_MS) { return; }
        lastTick = nowMs;
        if (dotnet && player) {
            const fin = (v) => (Number.isFinite(v) ? v : 0);
            dotnet.invokeMethodAsync('OnMoveTick', fin(player.position.x), fin(player.position.y), fin(player.position.z), fin(engine.getFps()));
        }
        if (player) { host.sendPose(player.position.x, player.position.z, player.rotation.y); }
    }

    // Boot do motor: cache local (OPFS) aquece antes da cena; GLB do catálogo assinado
    // entra em fire-and-forget; ouvintes de DOM só na main thread (no worker, o input
    // chega pela ponte de mensagens via api.input).
    async function boot() {
        const initStart = performance.now();
        try {
            if (G.game && G.game.assets) {
                await G.game.assets.init(host.build);
                await Promise.all([warmTextures(), warmTerrain()]);
            }
            engine = new BABYLON.Engine(host.canvas, true, { adaptToDeviceRatio: true, stencil: false });
            buildScene(host.canvas);
            // Cena pronta: o shell só resolve o game.init DAQUI para frente — o C# passa
            // a registrar modelos com a cena garantida (antes: "cena ainda não criada").
            if (host.onCoreReady) { host.onCoreReady(); }
            void glbLoadAll();   // assets GLB CC0 do catálogo assinado (fire-and-forget)
            if (dom) {
                setupTouchControls();
                window.addEventListener('gamepaddisconnected', () => { actionWasDown = false; cancelWasDown = false; setActiveInput('keyboard'); });
                window.addEventListener('keydown', onKeyDown);
                window.addEventListener('keyup', onKeyUp);
                window.addEventListener('blur', () => {
                    for (const k of Object.keys(keys)) { keys[k] = false; }
                    moveStick = { x: 0, y: 0 }; lookStick = { x: 0, y: 0 };
                });
                window.addEventListener('resize', () => engine.resize());
            }

            engine.runRenderLoop(() => {
                const frameStart = performance.now();
                drainModelQueue(frameStart);
                const delta = Math.min(0.05, engine.getDeltaTime() / 1000);
                const pad = dom ? pollGamepad() : padState;
                if (!frozen) {
                    camera.alpha -= (pad.lookX + lookStick.x) * delta * 2.2;
                    camera.beta = Math.max(0.18, Math.min(Math.PI / 2.15, camera.beta + (pad.lookY + lookStick.y) * delta * 1.7));
                    if (!dom) {
                        // Arrasto/roda do ponteiro chegaram pela ponte (sem attachControl).
                        if (camPending.dx || camPending.dy) {
                            camera.alpha -= camPending.dx * CAM_POINTER_SENS;
                            camera.beta = Math.max(0.18, Math.min(Math.PI / 2.15, camera.beta + camPending.dy * CAM_POINTER_SENS));
                            camPending.dx = 0; camPending.dy = 0;
                        }
                        if (camPending.wheel) {
                            camera.radius = Math.max(camera.lowerRadiusLimit || 2.4, Math.min(camera.upperRadiusLimit || 40, camera.radius * (1 + camPending.wheel * 0.01)));
                            camPending.wheel = 0;
                        }
                    }
                }
                movePlayer(delta, pad);
                if (player) { player.position.y = terrainHeight(player.position.x, player.position.z) + 0.03; }
                checkNear();
                camera.setTarget(player.position.add(CAM_UP));
                scene.render();
                framePerf(performance.now() - frameStart);
                tick(performance.now());
            });

            scene.executeWhenReady(() => {
                let rendererName = 'WebGL';
                try {
                    const glInfo = engine.getGlInfo();
                    if (glInfo && (glInfo.renderer || glInfo.vendor)) { rendererName = glInfo.renderer || glInfo.vendor; }
                } catch (e) { /* identificação não suportada */ }
                dotnet.invokeMethodAsync('OnEngineReady', String(rendererName));
                if (dom && host.canvas.focus) { host.canvas.focus(); }
            });
            if (dom) { window.__initMs = Math.round(performance.now() - initStart); }
        } catch (e) {
            console.error('[motor] falha no boot:', e);
            if (host.onCoreError) { host.onCoreError(String(e && e.message || e)); }
        }
    }

    // Entrada vinda da ponte (modo worker): teclas, analógicos virtuais, gamepad e
    // câmera por arrasto/roda; os handlers de DOM da main thread chamam os mesmos métodos.
    const input = {
        key(code, down) { if (gameCodes.includes(code)) { keys[code] = !!down; } },
        stick(which, x, y) { if (which === 'move') { moveStick = { x: x, y: y }; } else if (which === 'look') { lookStick = { x: x, y: y }; } },
        pad(x, y, lookX, lookY) { padState.x = x || 0; padState.y = y || 0; padState.lookX = lookX || 0; padState.lookY = lookY || 0; },
        camera(dx, dy, wheel) { camPending.dx += dx || 0; camPending.dy += dy || 0; camPending.wheel += wheel || 0; },
        action() { interact(); },
        cancel() { dotnet.invokeMethodAsync('OnClose'); },
        clear() { for (const k of Object.keys(keys)) { keys[k] = false; } moveStick = { x: 0, y: 0 }; lookStick = { x: 0, y: 0 }; },
        resize(w, h) { if (engine) { engine.resize(); } },
        inputMode(kind) { setActiveInput(kind); }
    };

    // No worker há dois assets.js (main e worker): o do MOTOR é o deste contexto.
    const assetsReal = G.game && G.game.assets ? G.game.assets : null;
    const CORE_ID = 'core-' + Math.random().toString(36).slice(2, 8);

    // Mescla em G.game (assets.js/net.js/... podem ter registrado antes).
    const api = {
        setFrozen(value) {
            frozen = !!value;
            if (frozen) { for (const k of Object.keys(keys)) { keys[k] = false; } }
        },
        resetPlayer() {
            if (player) { player.position.x = 0; player.position.z = 0; player.rotation.y = 0; }
            if (camera) { camera.alpha = -Math.PI / 2; camera.beta = 1.12; camera.radius = 7.5; }
        },
        teleportToGuardian() {
            if (!player || !camera) return false;
            player.position.x = NPC_POSITION.x;
            player.position.z = NPC_POSITION.z + 2.0;
            player.position.y = terrainHeight(player.position.x, player.position.z) + 0.03;
            player.rotation.y = Math.PI;
            camera.setTarget(player.position.add(CAM_UP));
            checkNear();
            return true;
        },
        addRemote,
        renameRemote,
        updateRemote,
        removeRemote,
        setMapTiles,
        setMapJobs,
        models: {
            // Chamado pelo ModelLoader (C#) com o descritor JSON de cada modelo. O trabalho
            // pesado entra na fila com orçamento por frame (game.perf.report() mostra o efeito).
            register(json, placeholder) {
                try {
                    const desc = JSON.parse(json);
                    if (!scene) { throw new Error('cena ainda não criada'); }
                    if (modelRegistry.has(desc.id)) { return false; }
                    enqueueModel(desc, placeholder);
                    return true;
                } catch (e) {
                    modelStatus.errors.push(String(e && e.message || e));
                    console.warn('[modelos] falha ao registrar', e);
                    return false;
                }
            },
            loaderDone(log) { modelStatus.log = Array.from(log || []); },
            status() { return JSON.parse(JSON.stringify(modelStatus)); },
            get(id) { const m = modelRegistry.get(id); return m ? m.desc : null; }
        },
        glb: {
            status() {
                const out = JSON.parse(JSON.stringify(glbStatus));
                out.instances = {};
                for (const [id, g] of glbRegistry) { out.instances[id] = g.proto.instances.length; }
                return out;
            },
            get(id) { const g = glbRegistry.get(id); return g ? g.entry : null; },
            // Costura para produtores futuros (IA interna, API, upload do jogador):
            // registra bytes GLB já confiáveis, normaliza (scaleM) e devolve o protótipo.
            async register(id, bytes, opts) {
                if (!scene) { throw new Error('cena ainda não criada'); }
                const existing = glbRegistry.get(id);
                if (existing) { return existing.proto; }
                const proto = await importGlbProto(id, bytes, (opts && opts.scaleM) || 1);
                if (opts && opts.shadows && sceneShadows) { sceneShadows.addShadowCaster(proto); }
                glbRegistry.set(id, { proto: proto, entry: { id: id, file: 'runtime:' + id, sha256: null } });
                glbStatus.loaded.push(id + ' (runtime)');
                return proto;
            }
        },
        perf: {
            // Custo de CPU do loop de render (sem incluir a espera de GPU): amostras dos
            // últimos 600 frames, medianas e máximos para validar o orçamento dos modelos.
            report() {
                const c = perfCpu.slice().sort((a, b) => a - b);
                const p = (q) => c.length ? c[Math.min(c.length - 1, Math.floor(c.length * q))] : 0;
                return {
                    budgetMs: MODEL_BUDGET_MS,
                    batch: MODEL_SCATTER_BATCH,
                    frames: perfCpu.length,
                    medianaMs: p(0.5),
                    p95Ms: p(0.95),
                    p99Ms: p(0.99),
                    maxMs: Math.round(perfCpuMax * 100) / 100,
                    over33: perfCpu.filter((v) => v > 33).length
                };
            }
        },
        input,
        assetsReport() { return assetsReal ? assetsReal.report() : null; },
        coreId() { return CORE_ID; }
    };
    G.game = G.game || {};
    Object.assign(G.game, api);
    if (assetsReal) { G.game.assets = assetsReal; }
    boot();
    return api;
        }
    };
})();
