// Ultima — Oficina do mundo (PLAN-PLAYER-ASSETS, fases 3 e 4): geração de modelos por
// prompt e upload de GLBs dos jogadores no catálogo do servidor (SHA-256 em tudo). O
// catálogo é compartilhado: assets novos aparecem para todos os clientes em posições
// determinísticas (derivadas do id). Sem backend — ou sem a rota /v1/assets — a oficina
// avisa e o jogo segue normal: nada aqui é obrigatório para jogar.
(function () {
    'use strict';

    const SYNC_MS = 30000;   // reler o catálogo do mundo a cada 30 s
    const MAX_BYTES = 2 * 1024 * 1024;
    let dotnet = null;
    let timer = 0;
    const known = new Set();   // ids já trazidos para a cena

    function live() {
        return (window.game && game.live) ? game.live : null;
    }

    function status(text) {
        if (dotnet) { dotnet.invokeMethodAsync('OnWorkshopStatus', String(text || '').slice(0, 160)); }
    }

    // Baixa um asset do servidor com verificação de SHA-256 (mesma regra do catálogo
    // local), registra o protótipo e espalha em posições determinísticas por id.
    async function bring(asset) {
        if (known.has(asset.id)) { return true; }
        const l = live();
        if (!l || !game.glb) { return false; }
        const bytes = await game.glb.fetchVerified({
            id: asset.id, file: asset.file, sha256: asset.sha256,
            baseUrl: l.api() + '/v1/assets/'
        });
        await game.glb.register(asset.id, bytes, { scaleM: asset.scaleM || 1, shadows: true });
        game.glb.scatter(asset.id, { count: 3, minScale: 0.85, maxScale: 1.25, sink: 0.02 });
        known.add(asset.id);
        return true;
    }

    async function sync() {
        const l = live();
        if (!l) { return; }
        try {
            const cat = await l.call('GET', '/v1/assets', undefined, 8000);
            for (const asset of (cat && cat.assets) || []) {
                bring(asset).catch(function (e) {
                    console.warn('[oficina] asset do mundo recusado, sigo sem ele:', asset.id, e);
                });
            }
        } catch (e) {
            // 404 = servidor ainda sem a rota de assets; offline = API fora. Silencioso:
            // o painel mostra o estado quando o jogador tenta usar a oficina.
        }
    }

    // Geração por prompt (fase 3): motor local devolve na hora; Meshy (se configurado no
    // servidor) responde 202 + job, e este poll conduz até o asset ficar pronto.
    async function generate(prompt) {
        const l = live();
        const clean = String(prompt || '').trim().slice(0, 120);
        if (!clean) { return 'Escreva o que você quer criar.'; }
        if (!l) { return 'Oficina indisponível.'; }
        try {
            let res = await l.call('POST', '/v1/assets/generate', { prompt: clean }, 30000);
            if (res && res.job && res.engine === 'meshy') {
                status('Gerando com IA na nuvem — leva alguns minutos…');
                for (let attempt = 0; attempt < 100; attempt++) {
                    await new Promise((r) => setTimeout(r, 3000));
                    const job = await l.call('GET', '/v1/assets/jobs/' + res.job, undefined, 10000);
                    if (job && job.status === 'done' && job.asset) { res = { asset: job.asset }; break; }
                    if (job && job.status === 'error') { return 'A geração falhou: ' + (job.error || 'erro'); }
                }
                if (!res.asset) { return 'A geração demorou demais; tente de novo.'; }
            }
            if (!res || !res.asset) { return 'O servidor não devolveu um modelo.'; }
            await bring(res.asset);
            return '✓ "' + clean + '" criado e plantado no mundo!';
        } catch (e) {
            if (e && (e.status === 404 || e.status === 501 || e.status === 503)) { return 'Este servidor ainda não tem oficina de modelos.'; }
            if (e && e.status === 401) { return 'Conecte-se ao servidor do jogo primeiro.'; }
            return 'Falhou: ' + ((e && e.message) || e);
        }
    }

    // Upload de GLB do jogador (fase 4): lê o arquivo, envia os bytes com o token da
    // sessão e planta no mundo compartilhado quando o servidor aceita.
    async function upload(input) {
        const l = live();
        const file = input.files && input.files[0];
        if (!file) { return; }
        if (!l || !l.token()) { status('Conecte-se ao servidor do jogo para enviar.'); return; }
        if (file.size > MAX_BYTES) { status('Máximo 2 MB por modelo.'); input.value = ''; return; }
        status('Enviando ' + (file.size / 1024).toFixed(0) + ' KB…');
        try {
            const scale = Number(document.getElementById('workshop-scale') && document.getElementById('workshop-scale').value) || 1;
            const name = file.name.replace(/\.glb$/i, '').slice(0, 40);
            const res = await fetch(l.api() + '/v1/assets/upload?name=' + encodeURIComponent(name) + '&scale=' + scale, {
                method: 'POST',
                headers: { 'Content-Type': 'model/gltf-binary', 'X-Game-Token': l.token() },
                body: await file.arrayBuffer(),
                cache: 'no-store'
            });
            const data = await res.json().catch(() => null);
            if (!res.ok) {
                const why = data && data.error === 'too_many_triangles' ? ' (muitos triângulos: máx. 30 mil)' :
                    data && data.error === 'not_a_glb' ? ' (arquivo não é um GLB válido)' : '';
                status('Enviado recusado' + why + '.');
            } else if (data && data.asset) {
                await bring(data.asset);
                status('✓ "' + name + '" publicado no mundo!');
            }
        } catch (e) {
            status('Falha no envio: ' + ((e && e.message) || e));
        }
        input.value = '';
    }

    window.game = window.game || {};
    window.game.workshop = {
        init(dotnetRef) {
            dotnet = dotnetRef;
            if (timer) { clearInterval(timer); }
            sync();
            timer = setInterval(sync, SYNC_MS);
        },
        generate: generate,
        upload: upload,
        status: function () { return { known: Array.from(known) }; }
    };
})();
