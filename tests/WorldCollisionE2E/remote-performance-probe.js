// O relógio, a atualização e as amostras ficam no mesmo contexto de renderização.
({ avatars, moving, warmup, samples }) => new Promise((resolve, reject) => {
    const scene = BABYLON.EngineStore.LastCreatedScene;
    const engine = scene.getEngine();
    const update = (avatar, step) => game.updateRemote(avatar.id, step ? avatar.nextX : avatar.x, avatar.z, step ? 0.8 : 0);
    for (const avatar of avatars) {
        game.addRemote(avatar.id, avatar.name);
        update(avatar, 0);
    }

    let frame = 0;
    function cleanup() {
        engine.onEndFrameObservable.remove(hook);
        globalThis.__remotePerf = null;
        for (const avatar of avatars) game.removeRemote(avatar.id);
    }

    const timer = setTimeout(() => {
        cleanup();
        reject(new Error('Amostragem excedeu o tempo limite'));
    }, 90000);
    const hook = engine.onEndFrameObservable.add(() => {
        frame++;
        if (moving && frame % 12 === 0) {
            for (const avatar of avatars) update(avatar, (frame / 12) % 2);
        }
        if (frame === warmup) {
            globalThis.__remotePerf = { rows: [], wasm: 0, calls: 0, previous: performance.now() };
        }
        if (frame !== warmup + samples) return;
        clearTimeout(timer);
        const rows = globalThis.__remotePerf.rows;
        const sun = scene.getLightByName('sun');
        const map = (sun.getShadowGenerator(scene.activeCamera) ?? sun.getShadowGenerator()).getShadowMap();
        const remoteShadowCasters = map.renderList.filter(mesh => mesh.name.includes('remote-perf-')).length;
        const enabledAvatars = avatars
            .filter(avatar => scene.getTransformNodeByName('remote-' + avatar.id).isEnabled()).length;
        cleanup();
        resolve({ rows, remoteShadowCasters, enabledAvatars });
    });
})
