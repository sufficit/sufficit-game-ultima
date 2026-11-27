// The player UUID is shared by tabs. Each tab keeps its own network peer and game session.
(function () {
    'use strict';

    const key = 'ultima-player-uuid';
    const valid = value => /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value ?? '');
    let uuid;

    function create() {
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
        return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
    }

    window.game.player = {
        uuid() {
            try {
                const stored = localStorage.getItem(key);
                if (valid(stored)) return stored;
                uuid ??= create();
                localStorage.setItem(key, uuid);
            } catch { uuid ??= create(); }
            return uuid;
        }
    };
})();
