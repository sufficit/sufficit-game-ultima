// Ultima — convite: copia/compartilha o link da sala P2P atual.
(function () {
    'use strict';
    window.game.net = window.game.net || {};
    window.game.net.copyInvite = async function () {
        const info = window.game.net.info ? window.game.net.info() : { room: 'jardim' };
        const url = new URL(location.href);
        url.searchParams.set('sala', info.room);
        url.hash = '';
        const link = url.toString();
        try {
            await navigator.clipboard.writeText(link);
            return 'Convite copiado. Abra o link em outra aba ou dispositivo: ' + info.room;
        } catch (e) {
            return 'Copie o link: ' + link;
        }
    };
})();
