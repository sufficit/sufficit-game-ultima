export function createAvatar(B, scene, descriptor, anatomy) {
    const root = new B.TransformNode('environment-player', scene);
    root.metadata = { modelId: descriptor.id, category: descriptor.category };
    const renderer = globalThis.UltimaCharacterRig.createRenderer(B, scene, () => {});
    const rig = renderer.create(root, false, false, anatomy);

    const materials = new Map(descriptor.materials.map(definition => {
        const material = new B.StandardMaterial('avatar-' + definition.id, scene);
        material.diffuseColor = new B.Color3(...definition.color);
        material.emissiveColor = new B.Color3(...definition.emissive);
        material.specularColor.set(definition.specular, definition.specular, definition.specular);
        return [definition.id, material];
    }));

    const protos = descriptor.groups.map(group => {
        const parts = group.parts.map(part => {
            const mesh = createPart(part);
            mesh.position.set(part.x, part.baseY + part.height * part.scaleY / 2, part.z);
            mesh.scaling.set(part.scaleX, part.scaleY, part.scaleZ);
            return mesh;
        });
        const proto = parts.length === 1 ? parts[0] : B.Mesh.MergeMeshes(parts, true, true);
        if (parts.length === 1) proto.bakeCurrentTransformIntoVertices();
        proto.name = 'model-' + descriptor.id + '-' + group.id;
        proto.material = materials.get(group.material);
        proto.isVisible = false;
        return proto;
    });
    renderer.mount(rig, descriptor.id, protos);
    for (const proto of protos) proto.dispose();

    const shadow = B.MeshBuilder.CreateGround('player-contact-shadow', { width: .9, height: .68 }, scene);
    shadow.parent = root;
    shadow.position.y = .035;
    shadow.isPickable = false;
    const shadowTexture = new B.DynamicTexture('player-shadow-opacity', 64, scene, false);
    const context = shadowTexture.getContext();
    const gradient = context.createRadialGradient(32, 32, 0, 32, 32, 31);
    gradient.addColorStop(0, 'rgba(15,19,22,0.35)');
    gradient.addColorStop(.6, 'rgba(15,19,22,0.14)');
    gradient.addColorStop(1, 'rgba(15,19,22,0)');
    context.fillStyle = gradient;
    context.fillRect(0, 0, 64, 64);
    shadowTexture.hasAlpha = true;
    shadowTexture.update();
    const shadowMaterial = new B.StandardMaterial('player-contact-material', scene);
    shadowMaterial.diffuseTexture = shadowTexture;
    shadowMaterial.useAlphaFromDiffuseTexture = true;
    shadowMaterial.disableLighting = true;
    shadowMaterial.emissiveColor.set(.15, .16, .17);
    shadow.material = shadowMaterial;

    return {
        setVisible(visible) { root.setEnabled(visible); },
        apply(frame) {
            const offset = 10;
            root.position.x = frame[0];
            root.position.z = frame[1];
            root.rotation.y = frame[2];
            renderer.applyLegs(rig, frame, frame, offset + 17, true, offset);
            renderer.applyUpperBody(rig, frame, true, offset);
            shadow.position.y = .035 - root.position.y;
        }
    };

    function createPart(part) {
        switch (part.shape) {
            case 'capsule':
                return B.MeshBuilder.CreateCapsule(part.id, { height: part.height, radius: part.diameterBottom / 2,
                    subdivisions: part.subdivisions, tessellation: part.crowdSides }, scene);
            case 'cylinder':
                return B.MeshBuilder.CreateCylinder(part.id, { height: part.height, diameterTop: part.diameterTop,
                    diameterBottom: part.diameterBottom, tessellation: part.tessellation }, scene);
            case 'sphere':
                return B.MeshBuilder.CreateSphere(part.id, { diameter: part.diameter, segments: part.tessellation }, scene);
            default:
                return B.MeshBuilder.CreateBox(part.id, { width: part.width, height: part.height, depth: part.depth }, scene);
        }
    }
}
