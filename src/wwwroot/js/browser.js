// Operações do navegador usadas pelos serviços C# do jogo.
window.game.browser = {
    build() { return document.querySelector('meta[name="ultima-build"]')?.content || 'dev'; },
    copy(text) { return navigator.clipboard.writeText(text); },
    pickFile(id) { document.getElementById(id)?.click(); },
    read(key) { try { return localStorage.getItem(key); } catch { return null; } },
    write(key, value) { try { localStorage.setItem(key, value); } catch { /* armazenamento opcional */ } },
    async reload() {
        try {
            const registrations = await navigator.serviceWorker?.getRegistrations() || [];
            for (const registration of registrations) {
                if (registration.scope === document.baseURI) { await registration.unregister(); }
            }
        } catch { /* recarrega mesmo assim */ }
        location.reload();
    }
};
