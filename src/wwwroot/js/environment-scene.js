export async function create(canvas, settings, selectedStation) {
    await window.game.loadBabylon();
    const B = window.BABYLON;
    const engine = new B.Engine(canvas, true, { adaptToDeviceRatio: true, stencil: false });
    const scene = new B.Scene(engine);
    scene.clearColor = B.Color4.FromHexString(settings.background + 'FF');

    const view = settings.camera;
    const camera = new B.ArcRotateCamera('environment-camera', view.alpha, view.beta, view.radius,
        new B.Vector3(view.targetX, view.targetY, view.targetZ), scene);
    camera.lowerBetaLimit = .15;
    camera.upperBetaLimit = 1.55;
    camera.lowerRadiusLimit = view.radius * .4;
    camera.upperRadiusLimit = view.radius * 2;
    camera.panningSensibility = 0;
    camera.attachControl(canvas, true);

    const fill = new B.HemisphericLight('environment-fill', new B.Vector3(0, 1, 0), scene);
    fill.intensity = .82;
    fill.groundColor = new B.Color3(.26, .31, .36);
    const daylight = new B.DirectionalLight('environment-daylight', new B.Vector3(.4, -1, -.6), scene);
    daylight.intensity = .52;

    const mats = new Map();
    const floorTags = new Map();
    function material(color) {
        if (!mats.has(color)) {
            const mat = new B.StandardMaterial(color, scene);
            mat.diffuseColor = B.Color3.FromHexString(color);
            mat.specularColor.set(.13, .14, .16);
            mat.freeze();
            mats.set(color, mat);
        }
        return mats.get(color);
    }

    for (const box of settings.boxes) {
        const mesh = B.MeshBuilder.CreateBox('environment-piece', {
            width: box.width, height: box.height, depth: box.depth,
        }, scene);
        mesh.position.set(box.x, box.y, box.z);
        mesh.material = material(box.color);
        mesh.isPickable = false;
        mesh.freezeWorldMatrix();
        if (box.station) {
            if (!floorTags.has(box.station)) floorTags.set(box.station, []);
            floorTags.get(box.station).push({ mesh, color: box.color });
        }
    }

    function select(station) {
        for (const [id, pieces] of floorTags)
            for (const { mesh, color } of pieces)
                mesh.material = material(station === id ? '#397B78' : color);
    }

    select(selectedStation);
    const resize = new ResizeObserver(() => engine.resize());
    resize.observe(canvas);
    engine.runRenderLoop(() => scene.render());
    return {
        select,
        dispose() {
            resize.disconnect();
            engine.stopRenderLoop();
            engine.dispose();
        },
    };
}
