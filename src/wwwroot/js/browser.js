// O Intl conhece os fusos IANA do dispositivo e suas regras históricas.
let localClock;
let localClockZone;
// Operações do navegador usadas pelos serviços C# do jogo.
window.game.browser = {
    build() { return document.querySelector('meta[name="ultima-build"]')?.content || 'dev'; },
    copy(text) { return navigator.clipboard.writeText(text); },
    pickFile(id) { document.getElementById(id)?.click(); },
    // Focus requested by Blazor: the window on open, the canvas on close. Without scrolling —
    // on screens with a virtual keyboard the default focus shifts the whole document.
    focus(element) { try { element.focus({ preventScroll: true }); } catch { /* focus is optional */ } },
    read(key) { try { return localStorage.getItem(key); } catch { return null; } },
    write(key, value) { try { localStorage.setItem(key, value); } catch { /* armazenamento opcional */ } },
    timeZones(saved) {
        const detected = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
        const zones = [detected, 'UTC', ...(Intl.supportedValuesOf?.('timeZone') ?? [])];
        if (saved && saved !== 'auto') {
            try {
                new Intl.DateTimeFormat('pt-BR', { timeZone: saved });
                zones.push(saved);
            } catch (error) { if (!(error instanceof RangeError)) throw error; }
        }
        return zones;
    },
    localTime(epoch, zone) {
        const effective = zone === 'auto' ? Intl.DateTimeFormat().resolvedOptions().timeZone : zone;
        if (!localClock || effective !== localClockZone) {
            localClock = new Intl.DateTimeFormat('pt-BR', {
                timeZone: effective, year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'shortOffset'
            });
            localClockZone = effective;
        }
        return localClock.format(epoch);
    },
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
