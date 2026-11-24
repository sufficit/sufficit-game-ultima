// Cache OPFS de artefatos preparados do Ultima (texturas procedurais, malha do terreno),
// com chave SHA-256 de {build do jogo, escopo, parâmetros} — mudança em qualquer um
// invalida o cache. Gravação via temporário + renome (atómica quando o navegador
// suporta move); leitura valida cabealho e tamanho, e qualquer corrupção vira cache
// miss (regenera). Padrão do prepare-worker do lineage-rust: OPFS + chave de hash +
// publicação atômica. O cache é pura otimização: a verificação de SHA-256 das DLLs
// em C# não passa por aqui.
(function () {
    'use strict';
    const DIR = 'ultima-assets';
    const MAGIC = 0x55414343;   // 'UACC'

    let dir = null;
    let build = '';
    const memory = new Map();   // chave -> { meta, parts } | null (null = miss conhecido)
    const stats = { opfs: false, hits: 0, misses: 0, errors: 0, writes: 0, readBytes: 0, wroteBytes: 0 };

    function report() {
        const r = Object.assign({ build: build, keys: memory.size }, stats);
        return JSON.parse(JSON.stringify(r));
    }

    // buildOverride: no Web Worker não existe <meta> — o shell repassa o build por
    // mensagem (mesma origem de verdade que o workflow grava no index.html).
    async function init(buildOverride) {
        let meta = null;
        if (typeof document !== 'undefined') { meta = document.querySelector('meta[name="ultima-build"]'); }
        build = buildOverride || (meta && meta.content) || 'dev';
        try {
            if (!navigator.storage || !navigator.storage.getDirectory) { throw new Error('OPFS indisponível'); }
            const root = await navigator.storage.getDirectory();
            dir = await root.getDirectoryHandle(DIR, { create: true });
            stats.opfs = true;
        } catch (e) {
            dir = null;
            console.info('[assets] cache local indisponível; sigo gerando na hora:', e && e.message);
        }
        return report();
    }

    // Chave de cache = SHA-256 do build + escopo + parâmetros.
    async function digest(parts) {
        const data = new TextEncoder().encode(JSON.stringify([build].concat(parts)));
        const buf = await crypto.subtle.digest('SHA-256', data);
        return Array.from(new Uint8Array(buf), (b) => b.toString(16).padStart(2, '0')).join('');
    }

    function encode(meta, parts) {
        const head = JSON.stringify(meta);
        const hb = new TextEncoder().encode(head);
        const total = 8 + hb.length + parts.reduce((a, p) => a + p.byteLength, 0);
        const buf = new ArrayBuffer(total);
        const dv = new DataView(buf);
        dv.setUint32(0, MAGIC, true);
        dv.setUint32(4, hb.length, true);
        new Uint8Array(buf, 8, hb.length).set(hb);
        let off = 8 + hb.length;
        for (const part of parts) {
            const bytes = ArrayBuffer.isView(part)
                ? new Uint8Array(part.buffer, part.byteOffset, part.byteLength)
                : new Uint8Array(part);
            new Uint8Array(buf, off, bytes.length).set(bytes);
            off += bytes.length;
        }
        return buf;
    }

    function decode(buf) {
        try {
            if (buf.byteLength < 8) { return null; }
            const dv = new DataView(buf);
            if (dv.getUint32(0, true) !== MAGIC) { return null; }
            const jsonLen = dv.getUint32(4, true);
            const head = JSON.parse(new TextDecoder().decode(new Uint8Array(buf, 8, jsonLen)));
            let off = 8 + jsonLen;
            const parts = [];
            for (const len of head.parts) {
                if (off + len > buf.byteLength) { return null; }   // truncado/adulterado
                parts.push(buf.slice(off, off + len));
                off += len;
            }
            if (off !== buf.byteLength) { return null; }
            return { meta: head, parts: parts };
        } catch (e) { return null; }
    }

    // Leitura: NotFound = miss normal; conteúdo inválido = erro + miss.
    async function load(key) {
        if (memory.has(key)) { return memory.get(key); }
        if (!dir) { return null; }
        try {
            const fh = await dir.getFileHandle(key);
            const file = await fh.getFile();
            const decoded = decode(await file.arrayBuffer());
            if (!decoded) {
                stats.errors++;
                memory.set(key, null);
                return null;
            }
            stats.hits++;
            stats.readBytes += file.size;
            memory.set(key, decoded);
            return decoded;
        } catch (e) {
            stats.misses++;
            memory.set(key, null);
            return null;
        }
    }

    // Gravação: temporário + renome quando suportado; falha é silenciosa (é só cache).
    async function save(key, meta, parts) {
        if (!dir) { return false; }
        try {
            const buf = encode(meta, parts);
            const tmp = await dir.getFileHandle(key + '.tmp', { create: true });
            const w = await tmp.createWritable();
            await w.write(buf);
            await w.close();
            if (typeof tmp.move === 'function') {
                await tmp.move(key);
            } else {
                const fh = await dir.getFileHandle(key, { create: true });
                const w2 = await fh.createWritable();
                await w2.write(buf);
                await w2.close();
            }
            stats.writes++;
            stats.wroteBytes += buf.byteLength;
            return true;
        } catch (e) {
            stats.errors++;
            return false;
        }
    }

    // --- Tipos usados pelo jogo ---

    // Textura procedural: RGBA (Uint8ClampedArray) + heightmap opcional (Float32Array).
    async function loadTex(name, size) {
        const key = await digest(['tex', name, size]);
        const hit = await load(key);
        if (!hit || hit.meta.size !== size) { return null; }
        const rgba = new Uint8ClampedArray(hit.parts[0]);
        const h = hit.meta.hasH ? new Float32Array(hit.parts[1]) : null;
        return { name: name, size: size, rgba: rgba, h: h };
    }

    function saveTex(name, size, rgba, h) {
        return digest(['tex', name, size]).then(function (key) {
            const parts = [rgba];
            if (h) { parts.push(h); }
            return save(key, { v: 1, kind: 'tex', name: name, size: size, hasH: !!h, parts: parts.map(function (p) { return p.byteLength; }) }, parts);
        });
    }

    // Malha do terreno: arrays de Float32 (posições, cores, normais).
    async function loadTerrain(expectVertices) {
        const key = await digest(['terrain', expectVertices]);
        const hit = await load(key);
        if (!hit || hit.meta.vertices !== expectVertices) { return null; }
        return {
            positions: new Float32Array(hit.parts[0]),
            colors: new Float32Array(hit.parts[1]),
            normals: new Float32Array(hit.parts[2])
        };
    }

    function saveTerrain(expectVertices, positions, colors, normals) {
        // Aceita Float32Array ou Array comum (o getVerticesData do Babylon pode devolver
        // array normal); grava sempre como Float32 little-endian.
        const pos = Float32Array.from(positions);
        const col = Float32Array.from(colors);
        const nor = Float32Array.from(normals);
        return digest(['terrain', expectVertices]).then(function (key) {
            const parts = [pos.buffer, col.buffer, nor.buffer];
            return save(key, { v: 1, kind: 'terrain', vertices: expectVertices, parts: parts.map(function (p) { return p.byteLength; }) }, parts);
        });
    }

    const G = (typeof window !== 'undefined') ? window : self;
    G.game = G.game || {};
    G.game.assets = {
        init: init, digest: digest, load: load, save: save,
        loadTex: loadTex, saveTex: saveTex,
        loadTerrain: loadTerrain, saveTerrain: saveTerrain,
        report: report
    };
})();
