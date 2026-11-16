// Ultima — aviso de nova versão. O workflow grava build.json e troca __BUILD__ no index.html;
// aqui comparamos periodicamente e orientamos o jogador a atualizar.
(function () {
    'use strict';
    const current = (document.querySelector('meta[name="ultima-build"]') || {}).content || 'dev';
    window.ULTIMA_BUILD = current;
    if (current === 'dev' || current.indexOf('__') === 0) { return; }

    const base = document.baseURI;
    let shown = false;

    function banner(latest) {
        if (shown) { return; }
        shown = true;
        const box = document.createElement('div');
        box.setAttribute('role', 'alert');
        box.style.cssText = 'position:fixed;left:50%;top:0.6rem;transform:translateX(-50%);z-index:99;max-width:min(560px,94vw);' +
            'background:#10261f;color:#e9fff6;border:1px solid #38d9a9;border-radius:0.7rem;padding:0.7rem 1rem;font:0.9rem system-ui,sans-serif;box-shadow:0 6px 24px #0008';
        box.innerHTML = '<strong>Nova versão disponível</strong> (' + latest + ').<br>' +
            'Clique em <b>Atualizar agora</b> — ou pressione <kbd>Ctrl</kbd>+<kbd>F5</kbd> (no celular: feche e reabra a aba). ' +
            'Seu nome e identidade são mantidos.<br><span style="display:inline-block;margin-top:0.5rem"></span>';
        const go = document.createElement('button');
        go.textContent = 'Atualizar agora';
        go.style.cssText = 'margin-right:0.5rem;padding:0.35rem 0.8rem;border-radius:0.4rem;border:0;background:#38d9a9;color:#06241b;font-weight:700;cursor:pointer';
        go.onclick = async () => {
            try { if (window.caches) { for (const k of await caches.keys()) { await caches.delete(k); } } } catch (e) { /* ignora */ }
            try { for (const r of await navigator.serviceWorker.getRegistrations()) { await r.unregister(); } } catch (e) { /* ignora */ }
            location.reload();
        };
        const later = document.createElement('button');
        later.textContent = 'Depois';
        later.style.cssText = 'padding:0.35rem 0.8rem;border-radius:0.4rem;border:1px solid #38d9a9;background:transparent;color:#e9fff6;cursor:pointer';
        later.onclick = () => { box.remove(); shown = false; setTimeout(check, 10 * 60 * 1000); };
        box.lastChild.appendChild(go); box.lastChild.appendChild(later);
        document.body.appendChild(box);
    }

    async function check() {
        try {
            const res = await fetch(new URL('build.json', base).href + '?t=' + Date.now(), { cache: 'no-store' });
            if (!res.ok) { return; }
            const info = await res.json();
            if (info && info.build && info.build !== current) { banner(String(info.build).slice(0, 7)); }
        } catch (e) { /* offline: tenta depois */ }
    }

    setTimeout(check, 3000);
    setInterval(check, 60000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { check(); } });
})();
