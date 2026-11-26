// WebRTC positions use the coordinate system of one environment.
export function createRoomPresence(environment, renderer) {
    const visible = new Set();

    function remove(peer) {
        if (visible.delete(peer)) renderer.removeRemote(peer);
    }

    return {
        pose(x, z, ry) {
            return JSON.stringify({ t: 'pos', environment, x, z, ry });
        },
        update(peer, name, message) {
            // Older clients send unscoped positions from the Garden.
            const scene = message.environment === undefined ? 'garden' : message.environment;
            if (scene !== environment) {
                remove(peer);
                return;
            }

            const { x, z, ry } = message;
            if (![x, z, ry].every(Number.isFinite)) return;
            if (!visible.has(peer)) {
                renderer.addRemote(peer, name);
                visible.add(peer);
            }
            renderer.updateRemote(peer, x, z, ry);
        },
        remove
    };
}
