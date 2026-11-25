import { attachWalk } from './environment-walk.js';

export async function create(canvas, settings, selectedStation, eyeHeight) {
    await window.game.loadBabylon();
    const B = window.BABYLON;
    const engine = new B.Engine(canvas, true, { adaptToDeviceRatio: true, stencil: false });
    const scene = new B.Scene(engine);
    scene.clearColor = B.Color4.FromHexString(settings.background + 'FF');

    let walk;
    if (settings.walk) {
        const runtime = window.getDotnetRuntime(0);
        const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        walk = attachWalk(B, canvas, scene, settings.walk, eyeHeight, assembly.Ultima.Environments.EnvironmentExports);
    } else {
        const view = settings.camera;
        const camera = new B.ArcRotateCamera('environment-camera', view.alpha, view.beta, view.radius,
            new B.Vector3(view.targetX, view.targetY, view.targetZ), scene);
        camera.lowerBetaLimit = .15;
        camera.upperBetaLimit = 1.55;
        camera.lowerRadiusLimit = view.radius * .4;
        camera.upperRadiusLimit = view.radius * 2;
        camera.panningSensibility = 0;
        camera.attachControl(canvas, true);
    }

    const fill = new B.HemisphericLight('environment-fill', new B.Vector3(0, 1, 0), scene);
    fill.intensity = walk ? .72 : .82;
    fill.groundColor = new B.Color3(.26, .31, .36);
    const daylight = new B.DirectionalLight('environment-daylight', new B.Vector3(.4, -1, -.6), scene);
    daylight.intensity = walk ? .27 : .52;
    if (walk) {
        daylight.diffuse = new B.Color3(.80, .88, 1);
        scene.imageProcessingConfiguration.contrast = 1.08;
    }
    for (const source of settings.lights ?? []) {
        const light = new B.PointLight('interior-light', new B.Vector3(source.x, source.y, source.z), scene);
        light.diffuse = new B.Color3(1, .94, .84);
        light.range = 8;
        light.intensity = source.intensity;
    }

    const mats = new Map();
    const floorTags = new Map();
    const textures = new Map();
    function material(color, finish) {
        const key = color + '/' + (finish ?? '');
        if (!mats.has(key)) {
            const mat = new B.StandardMaterial(key, scene);
            mat.diffuseColor = B.Color3.FromHexString(color);
            mat.specularColor.set(.13, .14, .16);
            mat.maxSimultaneousLights = 5;
            if (finish === 'glass') {
                mat.alpha = .25;
                mat.specularPower = 90;
                mat.backFaceCulling = false;
            }
            if (finish === 'light' || finish === 'screen') {
                mat.emissiveColor = mat.diffuseColor.scale(finish === 'light' ? .8 : .45);
            }
            if (['wood', 'carpet', 'stone'].includes(finish)) {
                if (!textures.has(finish)) {
                    const textureUrl = new URL('../materials/interiors/' + finish + '.webp', import.meta.url);
                    const texture = new B.Texture(textureUrl.href, scene);
                    texture.anisotropicFilteringLevel = 8;
                    textures.set(finish, texture);
                }
                mat.diffuseTexture = textures.get(finish);
                mat.specularColor.scaleInPlace(finish === 'carpet' ? .07 : .65);
            }
            mat.freeze();
            mats.set(key, mat);
        }
        return mats.get(key);
    }

    const opaque = new Map();
    for (const box of settings.boxes) {
        const mesh = B.MeshBuilder.CreateBox('environment-piece', {
            width: box.width, height: box.height, depth: box.depth,
        }, scene);
        mesh.position.set(box.x, box.y, box.z);
        mesh.material = material(box.color, box.material);
        mesh.isPickable = false;
        mesh.freezeWorldMatrix();
        if (walk && box.material !== 'glass') {
            if (!opaque.has(mesh.material)) opaque.set(mesh.material, []);
            opaque.get(mesh.material).push(mesh);
        }
        if (box.station && !walk) {
            if (!floorTags.has(box.station)) floorTags.set(box.station, []);
            floorTags.get(box.station).push({ mesh, color: box.color, finish: box.material });
        }
    }

    for (const group of opaque.values()) {
        if (group.length < 2) continue;
        const combined = B.Mesh.MergeMeshes(group, true, true);
        if (combined) {
            combined.isPickable = false;
            combined.freezeWorldMatrix();
        }
    }

    for (const sign of settings.signs ?? []) {
        const plate = B.MeshBuilder.CreatePlane('environment-sign', { width: sign.width, height: sign.height }, scene);
        plate.position.set(sign.x, sign.y, sign.z);
        plate.rotation.y = Math.PI;
        const graphic = new B.DynamicTexture('environment-sign-text', { width: 768, height: 192 }, scene);
        const context = graphic.getContext();
        context.fillStyle = '#28332f';
        context.fillRect(0, 0, 768, 192);
        context.fillStyle = '#f6ead7';
        context.font = '600 72px sans-serif';
        context.textAlign = 'center';
        context.textBaseline = 'middle';
        context.fillText(sign.text, 384, 98, 700);
        graphic.update();
        const label = new B.StandardMaterial('environment-sign-material', scene);
        label.diffuseTexture = label.emissiveTexture = graphic;
        label.emissiveColor.set(.58, .58, .58);
        plate.material = label;
        plate.isPickable = false;
    }

    function select(station) {
        for (const [id, pieces] of floorTags)
            for (const { mesh, color, finish } of pieces)
                mesh.material = material(station === id ? '#397B78' : color, finish);
    }

    select(selectedStation);
    const resize = new ResizeObserver(() => engine.resize());
    resize.observe(canvas);
    engine.runRenderLoop(() => {
        if (walk) walk.step(Math.min(.05, engine.getDeltaTime() / 1000));
        scene.render();
    });
    return {
        select,
        dispose() {
            resize.disconnect();
            walk?.dispose();
            engine.stopRenderLoop();
            engine.dispose();
        },
    };
}
