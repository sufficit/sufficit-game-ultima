// UltimaCore — motor do Ultima (cena Babylon.js: terreno procedural, fila de modelos
// com orçamento por frame, assets GLB CC0, NPCs visuais) SEM dependência de DOM:
// roda na main thread (fallback) ou em Web Worker com OffscreenCanvas (game-worker.js).
// host = { canvas, mode: 'main'|'worker', build, global, dom, postEvent, sendPose, onInputMode }.
(function () {
    'use strict';
    const ROOT = (typeof window !== 'undefined') ? window : self;   // main × worker
    ROOT.UltimaCore = {
        create: function (host) {

    const WORLD_HALF = 400;   // F3: mundo expansível (montanhas do gerador são a barreira natural)
    const SCATTER_HALF = 52;   // raio do espalhamento inicial (vale central)
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
    // Estilo do cenário (PLAN-PLAYER-ASSETS fase 5): 'procedural' (DLLs C#, padrão) ou
    // 'lowpoly' (GLBs CC0 alternativos substituem árvores/pedras/grama). Vem do host no
    // boot (a preferência mora no localStorage da página); a troca pede recarga.
    const sceneryStyle = host.scenery === 'lowpoly' ? 'lowpoly' : 'procedural';
    // F7b: 'padrao' (StandardMaterial convertido, iluminação atual) ou 'pbr' (materiais
    // glTF originais + IBL de um env procedural). Padrão = padrao; troca pede recarga.
    const graphicsStyle = host.graphics === 'pbr' ? 'pbr' : 'padrao';
    // id do modelo procedural -> id do asset GLB que o substitui no estilo lowpoly
    const SCENERY_ALT = { 'tree.pine': 'flora.pine', 'rock.boulder': 'rock.boulder2', 'scenery.grass': 'scenery.grass2' };

    let engine = null;
    let scene = null;
    let sceneShadows = null;
    let player = null;
    let camera = null;
    const G = host.global;
    const dom = !!host.dom;
    const CAM_POINTER_SENS = 0.01;    // rad/px do arrasto no worker — mesma medida do attachControl na main (200 px ≈ 2,0 rad)
    const camPending = { dx: 0, dy: 0, wheel: 0 };
    let zoomTarget = null;   // raio escolhido na roda (null = usa o raio atual)
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

    function baseHeight(x, z) {
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

    // ---------- Terreno em chunks (F2) + edição de superfície (F4) ----------
    // Mundo dividido em chunks de 32 m (malha própria, criada na fila com orçamento por
    // frame): rebuilds locais ao editar, costura invisível (alturas e normais amostradas
    // do MESMO gerador nas coordenadas de mundo — anel extra cobre as normais de borda)
    // e cache OPFS por chunk do TERRENO BASE. As ops sculpt/paint do servidor (journal)
    // são reaplicadas em memória a cada visita, depois do mapa chegar.
    const CHUNK = 32, CHUNK_SUB = 48;
    const CHUNK_VIEW = 2;                  // janela 5×5 ao redor do jogador (streaming F3)
    const chunkMap = new Map();            // "cx,cz" -> { mesh, cx, cz }
    const chunkCacheHits = new Map();      // "cx,cz" -> { positions, colors, normals }
    let editsAppliedRev = -1;              // revisão do servidor já refletida no terreno

    // Ops de edição (F4): esculturas somam altura com falloff suave; pinturas trocam o
    // material do solo num raio. terrainHeight as incorpora — jogador, câmera,
    // espalhamento e chunks passam a respeitar as edições automaticamente.
    const worldSculpts = [];
    const worldPaints = [];
    const worldCarves = [];   // F5b base: grutas {x,z,r,y0,y1} — piso absoluto + rocha
    const PAINT_COLORS = {
        grass: [0.30, 0.46, 0.16], dirt: [0.47, 0.36, 0.23], sand: [0.78, 0.70, 0.50],
        rock: [0.46, 0.45, 0.44], snow: [0.93, 0.95, 0.97], water: [0.16, 0.42, 0.62],
    };

    function brushWeight(x, z, op) {
        const t = 1 - Math.hypot(x - op.x, z - op.z) / op.r;
        return t <= 0 ? 0 : t * t * (3 - 2 * t);   // falloff suave na borda do pincel
    }

    // Altura efetiva do mundo: gerador base + esculturas (em ordem de journal) e grutas
    // (o miolo do carve vira piso absoluto — o jogador anda até o fundo da gruta).
    function terrainHeight(x, z) {
        let h = baseHeight(x, z);
        for (let i = 0; i < worldSculpts.length; i++) {
            const op = worldSculpts[i];
            const t = 1 - Math.hypot(x - op.x, z - op.z) / op.r;
            if (t > 0) { h += op.dh * t * t * (3 - 2 * t); }
        }
        for (let i = 0; i < worldCarves.length; i++) {
            const op = worldCarves[i];
            if (Math.hypot(x - op.x, z - op.z) < op.r && h > op.y0) {
                // dentro da gruta e abaixo do entorno original: chão plano em y0
                h = op.y0;
                for (let j = 0; j < worldSculpts.length; j++) {
                    const so = worldSculpts[j];
                    const t = 1 - Math.hypot(x - so.x, z - so.z) / so.r;
                    if (t > 0) { h += so.dh * t * t * (3 - 2 * t); }
                }
            }
        }
        return h;
    }

    // Paleta do solo (mesma lógica do gerador original), agora pintável por material.
    function terrainColorAt(x, y, z) {
        const grassDark = [0.16, 0.34, 0.13], grassLight = [0.38, 0.52, 0.20], grassDry = [0.55, 0.55, 0.26];
        const dirt = [0.47, 0.36, 0.23], dirtLight = [0.58, 0.47, 0.32];
        const rock = [0.42, 0.41, 0.40], rockLight = [0.55, 0.54, 0.52], snow = [0.93, 0.95, 0.97];
        const patch = noise(x * 0.11 + 100, z * 0.11 - 60);
        const fine = noise(x * 0.85 - 13.7, z * 0.85 + 77.1) * 0.5 + 0.5;
        const t = Math.max(0, Math.min(1, y * 0.16 + 0.42 + patch * 0.45 + (fine - 0.5) * 0.18));
        let r = lerp(lerp(grassDark[0], grassLight[0], t), grassDry[0], Math.max(0, patch - 0.35) * 1.4);
        let g = lerp(lerp(grassDark[1], grassLight[1], t), grassDry[1], Math.max(0, patch - 0.35) * 1.4);
        let b = lerp(lerp(grassDark[2], grassLight[2], t), grassDry[2], Math.max(0, patch - 0.35) * 1.4);
        const path = pathBlend(x, z);
        const core = 1 - smoothstep(0.4, 2.6, Math.abs(x - pathOffset(z)));
        const dr = lerp(dirtLight[0], dirt[0], path), dg = lerp(dirtLight[1], dirt[1], path), db = lerp(dirtLight[2], dirt[2], path);
        r = lerp(dr, r, path); g = lerp(dg, g, path); b = lerp(db, b, path);
        r = lerp(r, dr * 1.08, (1 - path) * core * 0.5);
        g = lerp(g, dg * 1.08, (1 - path) * core * 0.5);
        b = lerp(b, db * 1.08, (1 - path) * core * 0.5);
        const slope = slopeAt(x, z);
        const rocky = Math.max(0, Math.min(1, (slope - 0.55) / 0.55));
        r = lerp(r, lerp(rock[0], rockLight[0], fine), rocky);
        g = lerp(g, lerp(rock[1], rockLight[1], fine), rocky);
        b = lerp(b, lerp(rock[2], rockLight[2], fine), rocky);
        const snowLine = 8.5 + noise(x * 0.18 + 7.7, z * 0.18 - 3.3) * 1.6 - rocky * 1.2;
        const snowy = Math.max(0, Math.min(1, (y - snowLine) / 1.6));
        r = lerp(r, snow[0], snowy); g = lerp(g, snow[1], snowy); b = lerp(b, snow[2], snowy);
        for (let i = 0; i < worldPaints.length; i++) {
            const op = worldPaints[i];
            const w = brushWeight(x, z, op);
            if (w > 0) { const c = PAINT_COLORS[op.mat]; r = lerp(r, c[0], w); g = lerp(g, c[1], w); b = lerp(b, c[2], w); }
        }
        return [r, g, b];
    }

    function chunkKey(cx, cz) { return cx + ',' + cz; }

    // Aquecimento por aproximação: a janela de streaming enfileira o chunk ~32 m
    // antes de o jogador chegar — o OPFS lê nesse intervalo (com teto de memória).
    function prefetchChunkCache(cx, cz) {
        if (!(G.game && G.game.assets && G.game.assets.report().opfs)) { return; }
        if (chunkCacheHits.has(chunkKey(cx, cz))) { return; }
        if (chunkCacheHits.size > 64) {
            chunkCacheHits.delete(chunkCacheHits.keys().next().value);   // mais antigo fora
        }
        game.assets.digest(['chunk-v1', host.build, cx, cz]).then(function (key) {
            return game.assets.load(key).then(function (hit) {
                if (hit && hit.parts && hit.parts.length === 3) {
                    chunkCacheHits.set(chunkKey(cx, cz), {
                        positions: new Float32Array(hit.parts[0]),
                        colors: new Float32Array(hit.parts[1]),
                        normals: new Float32Array(hit.parts[2]),
                    });
                }
            });
        }).catch(function () { /* cache é otimização */ });
    }

    // O cache é do TERRENO BASE: nunca grava depois que edições são conhecidas.
    function saveChunk(cx, cz, positions, colors, normals) {
        if (!(G.game && G.game.assets) || worldSculpts.length || worldPaints.length) { return; }
        game.assets.digest(['chunk-v1', host.build, cx, cz]).then(function (key) {
            return game.assets.save(key,
                // O formato do cache (game.assets) declara cada parte em BYTES — como
                // fazem os outros saves (byteLength). Em elementos (¼ dos bytes de um
                // Float32Array) a leitura rejeitava o chunk e ele era regravado errado a
                // cada visita.
                { v: 1, kind: 'chunk', cx: cx, cz: cz, parts: [positions.byteLength, colors.byteLength, normals.byteLength] },
                [positions.buffer, colors.buffer, normals.buffer]);
        }).catch(function () { /* cache é otimização */ });
    }

    // Preenche um chunk: alturas do gerador, cores da paleta e normais analíticas com
    // anel extra — vértices de borda usam vizinhos reais, então chunks costuram sem emenda.
    function fillChunk(mesh, cx, cz) {
        const n = CHUNK_SUB + 1;
        const hit = chunkCacheHits.get(chunkKey(cx, cz));
        if (hit && hit.positions.length === n * n * 3 && hit.colors.length === n * n * 4 && hit.normals.length === n * n * 3) {
            mesh.setVerticesData(BABYLON.VertexBuffer.PositionKind, hit.positions);
            mesh.setVerticesData(BABYLON.VertexBuffer.ColorKind, hit.colors);
            mesh.setVerticesData(BABYLON.VertexBuffer.NormalKind, hit.normals);
            return;
        }
        const positions = new Float32Array(n * n * 3);
        const colors = new Float32Array(n * n * 4);
        const normals = new Float32Array(n * n * 3);
        const x0 = cx * CHUNK - CHUNK / 2, z0 = cz * CHUNK - CHUNK / 2;
        const step = CHUNK / CHUNK_SUB;
        const w = n + 2;                            // grade com anel extra para normais
        const hgt = new Float32Array(w * w);
        for (let j = -1; j <= n; j++) for (let i = -1; i <= n; i++) {
            hgt[(j + 1) * w + (i + 1)] = terrainHeight(x0 + i * step, z0 + j * step);
        }
        for (let j = 0; j < n; j++) for (let i = 0; i < n; i++) {
            const vi = j * n + i;
            const x = x0 + i * step, z = z0 + j * step;
            const h = hgt[(j + 1) * w + (i + 1)];
            positions[vi * 3] = x; positions[vi * 3 + 1] = h; positions[vi * 3 + 2] = z;
            const hx = hgt[(j + 1) * w + (i + 2)] - hgt[(j + 1) * w + i];
            const hz = hgt[(j + 2) * w + (i + 1)] - hgt[j * w + (i + 1)];
            const nl = Math.hypot(hx, step, hz) || 1;   // normal do heightfield
            normals[vi * 3] = -hx / nl; normals[vi * 3 + 1] = step / nl; normals[vi * 3 + 2] = -hz / nl;
            const c = terrainColorAt(x, h, z);
            colors[vi * 4] = c[0]; colors[vi * 4 + 1] = c[1]; colors[vi * 4 + 2] = c[2]; colors[vi * 4 + 3] = 1;
        }
        mesh.setVerticesData(BABYLON.VertexBuffer.PositionKind, positions);
        mesh.setVerticesData(BABYLON.VertexBuffer.ColorKind, colors);
        mesh.setVerticesData(BABYLON.VertexBuffer.NormalKind, normals);
        saveChunk(cx, cz, positions, colors, normals);
    }

    // Terreno em streaming (F3): janela de chunks ao redor do jogador na fila orçada;
    // chunks distantes são descartados (o cache OPFS devolve os conhecidos na volta).
    let lastEnsureKey = '';
    function enqueueChunkBuild(cx, cz) {
        prefetchChunkCache(cx, cz);
        const label = 'chunk ' + chunkKey(cx, cz);
        modelQueue.push({
            timing: { id: label, queuedAt: Math.round(performance.now()), cpuMs: 0, steps: 0, maxStepMs: 0 },
            work: (function* () {
                yield {
                    label: label,
                    run: function () {
                        const mesh = BABYLON.MeshBuilder.CreateGround('ground-' + chunkKey(cx, cz), { width: CHUNK, height: CHUNK, subdivisions: CHUNK_SUB }, scene);
                        fillChunk(mesh, cx, cz);
                        mesh.material = groundMaterial();
                        mesh.receiveShadows = true;
                        mesh.isPickable = true;
                        chunkMap.set(chunkKey(cx, cz), { mesh: mesh, cx: cx, cz: cz });
                    }
                };
            })()
        });
        modelStatus.queue = modelQueue.length;
    }

    function ensureChunksAround(px, pz) {
        if (!scene) { return; }
        const ccx = Math.floor(px / CHUNK), ccz = Math.floor(pz / CHUNK);
        const key = ccx + ',' + ccz;
        if (key === lastEnsureKey) { return; }
        lastEnsureKey = key;
        for (let dx = -CHUNK_VIEW; dx <= CHUNK_VIEW; dx++) for (let dz = -CHUNK_VIEW; dz <= CHUNK_VIEW; dz++) {
            if (!chunkMap.has(chunkKey(ccx + dx, ccz + dz))) { enqueueChunkBuild(ccx + dx, ccz + dz); }
        }
        for (const [k, ch] of Array.from(chunkMap)) {
            if (Math.abs(ch.cx - ccx) > CHUNK_VIEW + 1 || Math.abs(ch.cz - ccz) > CHUNK_VIEW + 1) {
                ch.mesh.dispose();
                chunkMap.delete(k);
            }
        }
    }

    function buildTerrain() { ensureChunksAround(0, 0); }

    // Material do chão (granulação em ladrilhos de ~3 m) compartilhado por todos os chunks.
    function groundMaterial() {
        const groundMat = new BABYLON.StandardMaterial('groundMat', scene);
        groundMat.diffuseColor = new BABYLON.Color3(1, 1, 1);
        groundMat.specularColor = new BABYLON.Color3(0.02, 0.02, 0.02);
        const detail = makeDetailTexture();
        detail.uScale = CHUNK / 3; detail.vScale = CHUNK / 3;   // um ladrilho a cada ~3 m
        groundMat.diffuseTexture = detail;
        return groundMat;
    }

    // ---------- Aplicação de edições e objetos plantados (F4/F6) ----------
    function applySurfaceEdit(op) {
        if (op.kind === 'sculpt') { worldSculpts.push(op); }
        else if (op.kind === 'paint') { worldPaints.push(op); }
        else if (op.kind === 'carve') { worldCarves.push(op); }
        else { return false; }
        refreshChunksAround(op.x, op.z, op.r);
        if (op.mat === 'water') { syncWaterDiscs(); }
        if (op.kind === 'carve') { syncCarveMeshes(); }
        return true;
    }

    function refreshChunksAround(x, z, r) {
        for (const ch of chunkMap.values()) {
            const minX = ch.cx * CHUNK - CHUNK / 2, maxX = minX + CHUNK;
            const minZ = ch.cz * CHUNK - CHUNK / 2, maxZ = minZ + CHUNK;
            if (x + r < minX || x - r > maxX || z + r < minZ || z - r > maxZ) { continue; }
            refreshChunkRegion(ch.mesh, x, z, r);
        }
    }

    // Re-computa a região do pincel num chunk: altura, cor e normal analítica.
    function refreshChunkRegion(mesh, x, z, r) {
        const pos = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
        const col = mesh.getVerticesData(BABYLON.VertexBuffer.ColorKind);
        const nor = mesh.getVerticesData(BABYLON.VertexBuffer.NormalKind);
        if (!pos || !col || !nor) { return; }
        const step = CHUNK / CHUNK_SUB;
        for (let i = 0; i < pos.length; i += 3) {
            const px = pos[i], pz = pos[i + 2];
            if (Math.hypot(px - x, pz - z) > r + step) { continue; }
            const py = terrainHeight(px, pz);
            pos[i + 1] = py;
            const c = terrainColorAt(px, py, pz);
            const ci = (i / 3) * 4;
            col[ci] = c[0]; col[ci + 1] = c[1]; col[ci + 2] = c[2]; col[ci + 3] = 1;
            const e = step;
            const hx = terrainHeight(px + e, pz) - terrainHeight(px - e, pz);
            const hz = terrainHeight(px, pz + e) - terrainHeight(px, pz - e);
            const nl = Math.hypot(hx / (2 * e), 1, hz / (2 * e)) || 1;
            nor[i] = -hx / (2 * e) / nl; nor[i + 1] = 1 / nl; nor[i + 2] = -hz / (2 * e) / nl;
        }
        mesh.updateVerticesData(BABYLON.VertexBuffer.PositionKind, pos);
        mesh.updateVerticesData(BABYLON.VertexBuffer.ColorKind, col);
        mesh.updateVerticesData(BABYLON.VertexBuffer.NormalKind, nor);
    }

    // Água pintada (F7a): discos translúcidos sobre o terreno, reposicionados a cada
    // rebuild (escavações posteriores afundam o disco junto).
    const waterDiscs = new Map();
    let waterTex = null;
    function waterNormalTexture() {
        if (waterTex) { return waterTex; }
        const N = 128;
        waterTex = new BABYLON.DynamicTexture('waterNrm', { width: N, height: N }, scene, true);
        const ctx = waterTex.getContext();
        const img = ctx.createImageData(N, N);
        const rnd = makeNoise(4242);
        for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
            const u = x / N * Math.PI * 2, v = y / N * Math.PI * 2;
            const dx = (rnd(Math.cos(u) * 4 + 1, Math.sin(v) * 4) - 0.5) * 0.9;
            const dy = (rnd(Math.cos(v) * 4 - 2, Math.sin(u) * 4 + 7) - 0.5) * 0.9;
            const inv = 1 / Math.hypot(dx, dy, 3), i = (y * N + x) * 4;
            img.data[i] = ((-dx * inv) * 0.5 + 0.5) * 255;
            img.data[i + 1] = ((-dy * inv) * 0.5 + 0.5) * 255;
            img.data[i + 2] = ((3 * inv) * 0.5 + 0.5) * 255;
            img.data[i + 3] = 255;
        }
        ctx.putImageData(img, 0, 0);
        waterTex.update(true);
        return waterTex;
    }
    function syncWaterDiscs() {
        for (const d of waterDiscs.values()) { d.dispose(); }
        waterDiscs.clear();
        for (const op of worldPaints) {
            if (op.mat !== 'water') { continue; }
            const disc = BABYLON.MeshBuilder.CreateDisc('water', { radius: op.r, tessellation: 24 }, scene);
            disc.rotation.x = Math.PI / 2;
            disc.isPickable = false;
            const m = new BABYLON.StandardMaterial('waterMat-' + waterDiscs.size, scene);
            m.diffuseColor = new BABYLON.Color3(0.10, 0.34, 0.55);
            m.specularColor = new BABYLON.Color3(0.7, 0.7, 0.7);
            m.alpha = 0.74;
            // F7b: normal de ondulação procedural animada + fresnel (brilho rasante do céu)
            m.bumpTexture = waterNormalTexture();
            m.bumpTexture.level = 0.6;
            m.emissiveColor = new BABYLON.Color3(0.02, 0.05, 0.08);
            m.emissiveFresnelParameters = new BABYLON.FresnelParameters();
            m.emissiveFresnelParameters.bias = 0.15;
            m.emissiveFresnelParameters.power = 2.2;
            m.emissiveFresnelParameters.leftColor = new BABYLON.Color3(0.0, 0.03, 0.06);
            m.emissiveFresnelParameters.rightColor = new BABYLON.Color3(0.45, 0.62, 0.78);
            disc.material = m;
            disc.position.set(op.x, terrainHeight(op.x, op.z) + 0.12, op.z);
            waterDiscs.set(disc, disc);
        }
    }

    // Vento (F7b-2): instâncias de vegetação balançam — fase por hash do nome, custo
    // amortizado (um terço das instâncias por frame).
    const windInstances = [];
    const WIND_IDS = ['scenery.bush', 'flora.fern', 'flora.flowers', 'scenery.grass2'];
    function collectWindInstances() {
        windInstances.length = 0;
        for (const [id, g] of glbRegistry) {
            if (!WIND_IDS.includes(id)) { continue; }
            for (const inst of g.proto.instances) { windInstances.push(inst); }
        }
    }
    let windCursor = 0, windFrames = 0;
    function windTick() {
        windFrames++;
        if (windFrames % 360 === 1) { collectWindInstances(); }   // repõe a cada ~6 s
        if (windInstances.length === 0) { return; }
        const t = performance.now() * 0.001;
        const third = Math.ceil(windInstances.length / 3);
        for (let n = 0; n < third; n++) {
            const inst = windInstances[windCursor];
            windCursor = (windCursor + 1) % windInstances.length;
            if (!inst || inst.isDisposed()) { continue; }
            let h = 0;
            for (const c of inst.name) { h = (h * 31 + c.charCodeAt(0)) | 0; }
            inst.rotation.z = Math.sin(t * 1.4 + (h & 31) * 0.7) * 0.035;
        }
    }

    // Grutas (F5b base): paredão de rocha (cilindro do piso à superfície do entorno)
    // e teto quando a gruta fica abaixo do terreno — malhas por op, ressincronizadas.
    const carveMeshes = [];
    function carveRockMaterial() {
        const m = new BABYLON.StandardMaterial('carve-rock', scene);
        m.diffuseColor = new BABYLON.Color3(0.34, 0.33, 0.32);
        m.specularColor = new BABYLON.Color3(0.04, 0.04, 0.04);
        return m;
    }
    function syncCarveMeshes() {
        for (const m of carveMeshes.splice(0)) { m.dispose(); }
        for (const op of worldCarves) {
            const rim = terrainHeight(op.x + op.r + 1, op.z);   // entorno fora do carve
            const wallH = Math.max(0.5, rim - op.y0);
            const wall = BABYLON.MeshBuilder.CreateCylinder('carve-wall', { height: wallH, diameter: op.r * 2 + 0.6, tessellation: 20 }, scene);
            wall.position.set(op.x, op.y0 + wallH / 2, op.z);
            wall.material = carveRockMaterial();
            wall.isPickable = false;
            wall.receiveShadows = true;
            carveMeshes.push(wall);
            if (op.y1 < rim - 0.5) {
                // sala subterrânea: teto de rocha (a entrada chega por passagem no nível y0)
                const ceil = BABYLON.MeshBuilder.CreateCylinder('carve-ceil', { height: 0.5, diameter: op.r * 2 + 0.4, tessellation: 20 }, scene);
                ceil.position.set(op.x, op.y1 + 0.25, op.z);
                ceil.material = carveRockMaterial();
                ceil.isPickable = false;
                carveMeshes.push(ceil);
            }
        }
    }

    // Replay do boot: limpa e reaplica a lista completa (chamado quando o mapa chega).
    function setWorldEdits(edits, revision) {
        if (revision !== undefined && revision === editsAppliedRev) { return; }
        if (revision !== undefined) { editsAppliedRev = revision; }
        worldSculpts.length = 0;
        worldPaints.length = 0;
        worldCarves.length = 0;
        for (const op of edits || []) { applySurfaceEdit(op); }
        syncWaterDiscs();
        syncCarveMeshes();
    }

    const placements = new Map();       // uid -> InstancedMesh
    const pendingPlacements = [];       // à espera do asset carregar

    function spawnPlacement(p) {
        if (!p || !p.uid || placements.has(p.uid)) { return; }
        const g = glbRegistry.get(p.asset);
        if (!g) {
            if (!pendingPlacements.some(function (q) { return q.uid === p.uid; })) { pendingPlacements.push(p); }
            return;
        }
        const inst = g.proto.createInstance('place-' + p.uid);
        inst.position.set(p.x, (p.y || terrainHeight(p.x, p.z)) - 0.02, p.z);
        inst.rotation.y = p.rot || 0;
        inst.scaling.setAll(p.scale || 1);
        if (sceneShadows) { try { sceneShadows.addShadowCaster(inst); } catch (e) { /* já fora */ } }
        placements.set(p.uid, inst);
    }

    function flushPendingPlacements() {
        for (const p of pendingPlacements.splice(0)) { spawnPlacement(p); }
    }

    function removePlacement(uid) {
        const inst = placements.get(uid);
        if (inst) {
            if (sceneShadows) { try { sceneShadows.removeShadowCaster(inst); } catch (e) { /* já fora */ } }
            inst.dispose();
            placements.delete(uid);
        }
        const ix = pendingPlacements.findIndex(function (q) { return q.uid === uid; });
        if (ix >= 0) { pendingPlacements.splice(ix, 1); }
    }

    function setPlacements(list) {
        const seen = new Set();
        for (const p of list || []) { if (p && p.uid) { seen.add(p.uid); spawnPlacement(p); } }
        for (const uid of Array.from(placements.keys())) { if (!seen.has(uid)) { removePlacement(uid); } }
    }

    function applyWorldOp(op) {
        if (!op) { return false; }
        if (op.kind === 'place') { spawnPlacement(op); return true; }
        if (op.kind === 'remove') { removePlacement(op.uid); return true; }
        if (op.rev !== undefined) { editsAppliedRev = op.rev; }
        return applySurfaceEdit(op);
    }

    // ---------- Modo construção: cliques no terreno viram ops (F4/F6) ----------
    const buildState = { mode: 'off', asset: '', r: 5, dh: 1.2, mat: 'sand', p1: null };

    function terrainClickImpl(sx, sy) {
        if (buildState.mode === 'off' || !scene || typeof host.worldOp !== 'function') { return; }
        const cid = 'c' + Math.random().toString(36).slice(2, 8);
        if (buildState.mode === 'remove') {
            const pick = scene.pick(sx, sy, function (m) { return !!(m && m.name && m.name.indexOf('place-') === 0); });
            const uid = pick && pick.hit ? pick.pickedMesh.name.slice(6) : null;
            if (uid) { host.worldOp({ action: 'remove', uid: uid, cid: cid }); }
            return;
        }
        const pick = scene.pick(sx, sy, function (m) { return !!(m && m.name && m.name.indexOf('ground-') === 0); });
        if (!pick || !pick.hit) { return; }
        const p = pick.pickedPoint;
        const r3 = function (v) { return Math.round(v * 1000) / 1000; };
        if (buildState.mode === 'passage') {
            if (!buildState.p1) {
                buildState.p1 = { x: r3(p.x), z: r3(p.z) };
                if (host.onBuildMode) { host.onBuildMode('passage-b'); }   // segundo clique
                return;
            }
            host.worldOp({ action: 'passage', x1: buildState.p1.x, z1: buildState.p1.z, x2: r3(p.x), z2: r3(p.z), r: buildState.r, dh: -1.5, cid: cid });
            buildState.p1 = null;
            if (host.onBuildMode) { host.onBuildMode('passage'); }
        } else if (buildState.mode === 'raise' || buildState.mode === 'dig') {
            host.worldOp({ action: 'sculpt', x: r3(p.x), z: r3(p.z), r: buildState.r, dh: r3(buildState.mode === 'raise' ? buildState.dh : -buildState.dh), cid: cid });
        } else if (buildState.mode === 'paint') {
            host.worldOp({ action: 'paint', x: r3(p.x), z: r3(p.z), r: buildState.r, mat: buildState.mat, cid: cid });
        } else if (buildState.mode === 'cave') {
            host.worldOp({ action: 'carve', x: r3(p.x), z: r3(p.z), r: Math.min(6, buildState.r + 1), y0: r3(p.y - 3), depth: 3, cid: cid });
        } else if (buildState.mode === 'place') {
            if (!buildState.asset) { return; }
            host.worldOp({ action: 'place', asset: buildState.asset, x: r3(p.x), y: r3(p.y), z: r3(p.z), rot: 0, scale: 1, cid: cid });
        }
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
                const x = (Math.random() * 2 - 1) * SCATTER_HALF;
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
            // No estilo lowpoly a versão GLB alternativa assume o espalhamento (fase 5).
            if (place && !(sceneryStyle === 'lowpoly' && SCENERY_ALT[desc.id])) { yield* place(protos, desc); }
        }
    }

    function enqueueModel(desc, placeholder) {
        modelQueue.push({
            timing: { id: desc.id, queuedAt: Math.round(performance.now()), cpuMs: 0, steps: 0, maxStepMs: 0 },
            work: modelWork(desc, placeholder)
        });
        modelStatus.queue = modelQueue.length;
    }

    // Descarta as malhas e instâncias de um modelo registrado (variação paramétrica troca
    // o cenário: o descritor antigo sai de cena inteiro antes do novo entrar na fila).
    function disposeModel(id) {
        const reg = modelRegistry.get(id);
        if (!reg) { return; }
        modelRegistry.delete(id);
        for (const p of reg.protos) {
            if (sceneShadows) { try { sceneShadows.removeShadowCaster(p); } catch (e) { /* já fora */ } }
            for (const inst of (p.instances || []).slice()) { inst.dispose(); }
            p.dispose();
        }
    }

    // Troca um modelo em cena pelo descritor JSON dado (mesmo id): descarta o antigo e
    // enfileira a reconstrução — texturas/malhas/espalhamento seguem o orçamento normal.
    function replaceModel(json) {
        try {
            const desc = JSON.parse(json);
            if (!scene) { throw new Error('cena ainda não criada'); }
            disposeModel(desc.id);
            enqueueModel(desc, false);
            return true;
        } catch (e) {
            modelStatus.errors.push('replace: ' + (e && e.message || e));
            console.warn('[modelos] falha ao trocar modelo', e);
            return false;
        }
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
                const x = (Math.random() * 2 - 1) * SCATTER_HALF;
                const z = (Math.random() * 2 - 1) * SCATTER_HALF;
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
            if (graphicsStyle !== 'pbr' || !envOk) { m.material = glbMaterialToStandard(m.material); }
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

    // Alternativas low-poly (fase 5): assumem o espalhamento com as mesmas regras e
    // contagens dos modelos procedurais que substituem — o mundo fica igual em forma.
    const glbAltPlacements = {
        'flora.pine': { scaleM: 7.0, shadows: true, place: (p) => batchedSteps('árvores low-poly', makeTreePlacer([p], 0.12, 64)) },
        'rock.boulder2': { scaleM: 1.9, shadows: true, place: (p) => batchedSteps('pedras low-poly', makeInstancePlacer(p, 26, { minScale: 0.6, maxScale: 2.2, maxAlt: 26, maxSlope: 0.9, sink: -0.18, minR: 6 })) },
        'scenery.grass2': { scaleM: 0.55, shadows: false, place: (p) => batchedSteps('grama low-poly', makeInstancePlacer(p, 420, { minScale: 0.7, maxScale: 1.5, maxAlt: 14, maxSlope: 0.55, sink: 0, minR: 4.5 })) }
    };

    // Um asset do catálogo: rede/hash fora da fila (I/O), o espalhamento (CPU real:
    // terreno/ruído por instância) entra na fila de modelos com o orçamento por frame.
    // No estilo lowpoly, os assets alternativos (glbAltPlacements) assumem o lugar dos
    // procedurais; no estilo padrão eles carregam sem espalhar (ficam de reserva).
    async function loadGlbAsset(entry) {
        const cfg = glbPlacements[entry.id];
        const alt = glbAltPlacements[entry.id];
        const bytes = await fetchVerifiedGlb(entry);
        const proto = await importGlbProto(entry.id, bytes, (alt || cfg) ? (alt || cfg).scaleM : 1);
        glbRegistry.set(entry.id, { proto: proto, entry: entry });
        glbStatus.loaded.push(entry.id);
        flushPendingPlacements();
        const active = (sceneryStyle === 'lowpoly' && alt) ? alt : (!alt ? cfg : null);
        if (active) {
            if (active.shadows && sceneShadows) { sceneShadows.addShadowCaster(proto); }
            modelQueue.push({
                timing: { id: entry.id, queuedAt: Math.round(performance.now()), cpuMs: 0, steps: 0, maxStepMs: 0 },
                work: active.place(proto)
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

    // ---------- Espalhamento determinístico (ativos do mundo compartilhado, fase 4) ----------
    // Posições derivadas do hash do id do asset: todos os clientes que veem o mesmo
    // catálogo do servidor veem o mesmo mundo — sem precisar de posições no servidor.
    function seedHash(str) {
        let h = 2166136261;
        for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
        return h >>> 0;
    }

    function mulberry32(a) {
        return function () {
            a |= 0; a = (a + 0x6D2B79F5) | 0;
            let t = Math.imul(a ^ (a >>> 15), 1 | a);
            t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
            return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
        };
    }

    function seededScatterWork(proto, count, opts, seed) {
        const rnd = mulberry32(seed);
        let placed = 0, tries = 0;
        return (function* () {
            while (true) {
                let done = false;
                yield {
                    label: 'oficina ' + proto.name + ' ' + placed + '/' + count,
                    run: function () {
                        let n = MODEL_SCATTER_BATCH;
                        while (placed < count && tries < count * 40 && n > 0) {
                            tries += 1;
                            const x = (rnd() * 2 - 1) * SCATTER_HALF;
                            const z = (rnd() * 2 - 1) * SCATTER_HALF;
                            const r = Math.hypot(x, z);
                            if (r < (opts.minR || 5)) { continue; }
                            if (Math.hypot(x - NPC_POSITION.x, z - NPC_POSITION.z) < 4.5) { continue; }
                            if (pathBlend(x, z) < 0.72) { continue; }
                            const y = terrainHeight(x, z);
                            if (opts.maxAlt !== undefined && y > opts.maxAlt) { continue; }
                            if (slopeAt(x, z) > (opts.maxSlope || 0.6)) { continue; }
                            const s = opts.minScale + rnd() * ((opts.maxScale || opts.minScale) - opts.minScale);
                            const inst = proto.createInstance(proto.name + '-w' + placed);
                            inst.position.set(x, y + (opts.sink || 0) * s, z);
                            inst.rotation.y = rnd() * Math.PI * 2;
                            inst.scaling.set(s, s * (0.9 + rnd() * 0.3), s);
                            placed += 1;
                            n -= 1;
                        }
                        done = placed >= count || tries >= count * 40;
                    }
                };
                if (done) { return; }
            }
        })();
    }

    // Espalha um asset já registrado em posições determinísticas (mesmas em todo cliente).
    function scatterSeeded(id, opts) {
        const g = glbRegistry.get(id);
        if (!g || !scene) { return false; }
        if (opts && opts.shadows && sceneShadows) { sceneShadows.addShadowCaster(g.proto); }
        modelQueue.push({
            timing: { id: id + ':espalhar', queuedAt: Math.round(performance.now()), cpuMs: 0, steps: 0, maxStepMs: 0 },
            work: seededScatterWork(g.proto, (opts && opts.count) || 3,
                {
                    minScale: (opts && opts.minScale) || 0.85, maxScale: (opts && opts.maxScale) || 1.25,
                    maxAlt: 16, maxSlope: 0.55, sink: (opts && opts.sink) !== undefined ? opts.sink : 0.02, minR: 6
                },
                seedHash(id))
        });
        modelStatus.queue = modelQueue.length;
        return true;
    }

    let envOk = false;
    function setupProceduralEnv() {
        try {
            const faces = {};
            const mk = function (top, mid, bottom) {
                const c = document.createElement ? document.createElement('canvas') : new OffscreenCanvas(64, 64);
                c.width = c.height = 64;
                const ctx = c.getContext('2d');
                const g = ctx.createLinearGradient(0, 0, 0, 64);
                g.addColorStop(0, top); g.addColorStop(0.55, mid); g.addColorStop(1, bottom);
                ctx.fillStyle = g;
                ctx.fillRect(0, 0, 64, 64);
                return c.toDataURL ? c.toDataURL('image/png') : '';
            };
            const sky = mk('#4d7dc4', '#9fc0e0', '#d3e2ee');
            const ground = mk('#8a9a7a', '#5c6e4a', '#3f4d33');
            faces.px = sky; faces.nx = sky; faces.pz = sky; faces.nz = sky;
            faces.py = sky; faces.ny = ground;
            const tex = new BABYLON.CubeTexture('', scene, faces, false);
            tex.name = 'env-procedural';
            scene.environmentTexture = tex;
            scene.environmentIntensity = 0.9;
            envOk = true;
        } catch (e) {
            console.warn('[graficos] env procedural indisponível, PBR sem IBL:', e && e.message);
        }
    }

    function buildScene(canvas) {
        scene = new BABYLON.Scene(engine);
        const horizon = new BABYLON.Color3(0.72, 0.80, 0.88);
        scene.clearColor = new BABYLON.Color4(horizon.r, horizon.g, horizon.b, 1);
        scene.fogMode = BABYLON.Scene.FOGMODE_EXP2;
        scene.fogColor = horizon;
        scene.fogDensity = 0.0105;
        scene.ambientColor = new BABYLON.Color3(0.34, 0.38, 0.42);

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
        shadows.darkness = 0.45;   // crateras continuam legíveis (F7a)

        skyDome();
        if (graphicsStyle === 'pbr') { setupProceduralEnv(); }
        buildTerrain();   // grade de chunks na fila com orçamento por frame
        scene.registerBeforeRender(function () {
            if (waterTex) { waterTex.uOffset += engine.getDeltaTime() * 0.00003; }
            windTick();
        });

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
                await warmTextures();   // chunks: aquecimento por aproximação (prefetchChunkCache)
            }
            // No worker o OffscreenCanvas não tem layout nem devicePixelRatio: o shell
            // mede (CSS × densidade) e o tamanho é aplicado daqui; sem isso o canvas
            // ficaria no 300×150 padrão esticado pela tela (regressão da migração).
            engine = new BABYLON.Engine(host.canvas, true, { adaptToDeviceRatio: dom, stencil: false });
            if (!dom && host.size) { applySize(host.size.width, host.size.height, host.size.dpr); }
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
                }
                if (!dom) {
                    // Ponteiro (arrasto/roda) chegou pela ponte — fora do travamento do
                    // diálogo, como o attachControl na main thread.
                    if (camPending.dx || camPending.dy) {
                        camera.alpha -= camPending.dx * CAM_POINTER_SENS;
                        camera.beta = Math.max(0.18, Math.min(Math.PI / 2.15, camera.beta + camPending.dy * CAM_POINTER_SENS));
                        camPending.dx = 0; camPending.dy = 0;
                    }
                    if (camPending.wheel) {
                        // Zoom equivalente ao attachControl com wheelDeltaPercentage:
                        // no Babylon, um entalhe (deltaY 120) ≈ 10% do raio somando a
                        // inércia (fator ~9× sobre 0,01·120·pct). Aplicamos o mesmo
                        // resultado DIRETO no alvo e suavizamos a chegada — sem a cauda
                        // de inércia acumulada, que continuava rodando sozinha.
                        // Proporção medida no attachControl (Babylon 9.36, inércia somada):
                        // um entalhe (120) afasta ≈12,1%; para perto, 7 entalhes ≈ −50%
                        // (≈9,9% cada). Em escala logarítmica o delta parcial do
                        // touchpad fica suave.
                        const entalhes = -camPending.wheel / 120;
                        if (zoomTarget === null) { zoomTarget = camera.radius; }
                        zoomTarget *= entalhes > 0 ? Math.pow(0.9015, entalhes) : Math.pow(1.1212, -entalhes);
                        zoomTarget = Math.max(camera.lowerRadiusLimit || 2.4, Math.min(camera.upperRadiusLimit || 40, zoomTarget));
                        camPending.wheel = 0;
                    }
                    if (zoomTarget !== null) {
                        // Chegada suave por tempo real (independente da taxa de quadros).
                        camera.radius += (zoomTarget - camera.radius) * Math.min(1, Math.min(0.25, engine.getDeltaTime() / 1000) * 14);
                        if (Math.abs(zoomTarget - camera.radius) < 1e-3) { camera.radius = zoomTarget; zoomTarget = null; }
                    }
                }
                movePlayer(delta, pad);
                if (player) {
                    player.position.y = terrainHeight(player.position.x, player.position.z) + 0.03;
                    ensureChunksAround(player.position.x, player.position.z);
                }
                checkNear();
                // Seguir mutando o vetor-alvo NO LUGAR: preserva alfa/beta/raio (a
                // distância que o jogador escolheu na roda). O setTarget recalcula o
                // raio a partir da posição atual da câmera — a cada frame com o jogador
                // em movimento o raio rangueava de volta ao inicial.
                camera.target.set(player.position.x, player.position.y + PLAYER_HEIGHT / 2, player.position.z);
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
        camera(dx, dy, wheel, deltaMode) { camPending.dx += dx || 0; camPending.dy += dy || 0; camPending.wheel += (wheel || 0) * (deltaMode === 1 ? 40 : 1); },
        action() { interact(); },
        terrainClick(sx, sy) { terrainClickImpl(sx, sy); },
        cancel() { dotnet.invokeMethodAsync('OnClose'); },
        clear() { for (const k of Object.keys(keys)) { keys[k] = false; } moveStick = { x: 0, y: 0 }; lookStick = { x: 0, y: 0 }; },
        resize(w, h, dpr) {
            if (!engine) { return; }
            if (dom) { engine.resize(); } else { applySize(w, h, dpr || (host.size && host.size.dpr) || 1); }
        },
        inputMode(kind) { setActiveInput(kind); }
    };

    // Tamanho de render no worker. Semântica do Babylon (AbstractEngine.resize/setSize):
    // setSize grava o canvas em PIXELS direto; o fator de escala só converte CSS↔pixels
    // no resize(). Logo: canvas = CSS × densidade, escala = 1/densidade — o mesmo buffer
    // que o adaptToDeviceRatio produz na main thread (getRenderWidth volta em CSS).
    function applySize(width, height, dpr) {
        if (!engine) { return; }
        const ratio = Math.max(0.25, dpr || 1);
        engine.setHardwareScalingLevel(1 / ratio);
        engine.setSize(Math.max(1, Math.floor((width || 1280) * ratio)), Math.max(1, Math.floor((height || 720) * ratio)));
    }

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
            if (camera) { camera.alpha = -Math.PI / 2; camera.beta = 1.12; camera.radius = 7.5; zoomTarget = null; }
        },
        teleportToGuardian() {
            if (!player || !camera) return false;
            player.position.x = NPC_POSITION.x;
            player.position.z = NPC_POSITION.z + 2.0;
            player.position.y = terrainHeight(player.position.x, player.position.z) + 0.03;
            player.rotation.y = Math.PI;
            camera.target.set(player.position.x, player.position.y + PLAYER_HEIGHT / 2, player.position.z);
            checkNear();
            return true;
        },
        addRemote,
        renameRemote,
        updateRemote,
        removeRemote,
        setMapTiles,
        setMapJobs,
        // Edição do mundo (F4/F6): replay do boot (mapa) e ops ao vivo (eventos).
        setWorldEdits(edits, revision) { setWorldEdits(edits, revision); },
        setPlacements(list) { setPlacements(list); },
        applyWorldOp(op) { return applyWorldOp(op); },
        // Sonda de altura (debug/validação): altura efetiva com as edições aplicadas.
        heightAt(x, z) { return Math.round(terrainHeight(x, z) * 100) / 100; },
        // Sonda do MESH: y do vértice mais próximo de (x,z) nos chunks em cena.
        probeMesh(x, z) {
            let best = null;
            for (const ch of chunkMap.values()) {
                const pos = ch.mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
                if (!pos) { continue; }
                for (let i = 0; i < pos.length; i += 3) {
                    const d = Math.hypot(pos[i] - x, pos[i + 2] - z);
                    if (best === null || d < best.d) { best = { d: d, y: pos[i + 1] }; }
                }
            }
            return best ? { d: Math.round(best.d * 100) / 100, y: Math.round(best.y * 100) / 100 } : null;
        },
        build: {
            mode(m, opts) {
                buildState.mode = m || 'off';
                buildState.p1 = null;
                if (host.onBuildMode) { host.onBuildMode(buildState.mode); }
                if (opts) {
                    if (opts.asset) { buildState.asset = String(opts.asset); }
                    if (opts.r) { buildState.r = Math.min(8, Math.max(0.5, Number(opts.r) || 5)); }
                    if (opts.dh) { buildState.dh = Math.min(2, Math.max(0.1, Number(opts.dh) || 1.2)); }
                    if (opts.mat) { buildState.mat = String(opts.mat); }
                }
                return buildState.mode;
            },
            state() { var st = JSON.parse(JSON.stringify(buildState)); st.chunks = chunkMap.size; return st; }
        },
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
            get(id) { const m = modelRegistry.get(id); return m ? m.desc : null; },
            // Variação paramétrica (PLAN-PLAYER-ASSETS fase 2): o C# (ponte do shell)
            // reconstrói o descritor com novos Seed/Detalhe e a cena troca + re-espalha.
            async vary(id, opts) {
                if (typeof host.varyModel !== 'function') { throw new Error('ponte de variação indisponível'); }
                const o = opts || {};
                const json = await host.varyModel(id, o.seed | 0, o.detail === undefined ? 1 : Number(o.detail));
                if (!json) { throw new Error('modelo ' + id + ' não carregado (DLL ausente)'); }
                return replaceModel(json);
            }
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
            // registra bytes GLB já confiáveis, normaliza (scaleM). Devolve true (o
            // protótipo é interno do motor — pela ponte do worker só viajam valores JSON).
            async register(id, bytes, opts) {
                if (!scene) { throw new Error('cena ainda não criada'); }
                if (glbRegistry.has(id)) { return false; }
                const proto = await importGlbProto(id, bytes, (opts && opts.scaleM) || 1);
                if (opts && opts.shadows && sceneShadows) { sceneShadows.addShadowCaster(proto); }
                glbRegistry.set(id, { proto: proto, entry: { id: id, file: 'runtime:' + id, sha256: null } });
                glbStatus.loaded.push(id + ' (runtime)');
                flushPendingPlacements();
                return true;
            },
            // Espalha um asset registrado em posições determinísticas por id (fase 4):
            // usado pela oficina (workshop.js) para os assets do mundo compartilhado.
            scatter(id, opts) { return scatterSeeded(id, opts); }
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
        renderSize() { return engine ? { width: engine.getRenderWidth(), height: engine.getRenderHeight(), canvasW: host.canvas.width, canvasH: host.canvas.height, scaling: engine.getHardwareScalingLevel() } : null; },
        cameraState() { return camera ? { alpha: camera.alpha, beta: camera.beta, radius: camera.radius } : null; },
        playerState() { return player ? { x: player.position.x, z: player.position.z, ry: player.rotation.y } : null; },
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
