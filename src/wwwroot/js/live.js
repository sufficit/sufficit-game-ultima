// Ponte entre a sessão HTTP em C# e as APIs do navegador e do motor.
(function () {
    'use strict';

    const fallbackApi = 'https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br';
    const query = new URLSearchParams(location.search);
    const api = (query.get('api') || window.ULTIMA_API || fallbackApi).replace(/\/+$/, '');
    const enabled = query.get('live') !== '0';
    const profileStorageKey = 'ultima-character-profile:' + api;
    let token = null;
    let bridge;
    let mapRevision = -1;

    window.addEventListener('pagehide', () => {
        if (!token) { return; }
        fetch(api + '/v1/game/leave', {
            method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Game-Token': token },
            body: '{}', keepalive: true
        }).catch(() => {});
    });

    window.game.live = {
        init(dotnet) { bridge = dotnet; },
        context() {
            let stored = null;
            try { stored = sessionStorage.getItem('ultima-live-token'); } catch { /* opcional */ }
            let profileKey = null;
            try { profileKey = localStorage.getItem(profileStorageKey); } catch { /* Storage can be disabled. */ }
            return { api, enabled, token: stored, profileKey, playerUuid: game.player.uuid() };
        },
        setProfileKey(value) {
            try { localStorage.setItem(profileStorageKey, value); } catch { /* Storage can be disabled. */ }
        },
        setSession(value) {
            if (!value) mapRevision = -1;
            token = value;
            try {
                if (token) { sessionStorage.setItem('ultima-live-token', token); }
                else { sessionStorage.removeItem('ultima-live-token'); }
            } catch { /* opcional */ }
        },
        api() { return api; },
        token() { return token; },
        pose(x, z, yaw) { if (token) { bridge?.invokeMethodAsync('OnLivePose', x, z, yaw).catch(() => {}); } },
        sendChat(text) { if (token) { bridge?.invokeMethodAsync('OnLiveChat', text).catch(() => {}); } },
        paintCharacters(characters) {
            for (const ch of characters) {
                game.addRemote(ch.id, ch.name);
                game.renameRemote(ch.id, ch.name);
                game.updateRemote(ch.id, ch.x, ch.z, ch.yaw);
            }
        },
        paintMap(map) {
            if (Number.isSafeInteger(map?.revision)) {
                if (map.revision < mapRevision) return;
                mapRevision = map.revision;
            }
            game.setMapTiles(map?.tiles || {});
            game.setMapJobs(map?.jobs || []);
            game.setWorldEdits(map?.edits || [], map?.revision ?? undefined);
            game.setPlacements(map?.placements || []);
        }
    };
})();
