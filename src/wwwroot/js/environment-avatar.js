export function createAvatar(B, scene, descriptor) {
    const root = new B.TransformNode('environment-player', scene);
    root.metadata = { modelId: descriptor.id, category: descriptor.category };
    const joints = ['leg-left', 'leg-right', 'arm-left', 'arm-right'].map((name, index) => {
        const joint = new B.TransformNode(name, scene);
        joint.parent = root;
        joint.position.y = index < 2 ? .77 : 1.4;
        return joint;
    });

    const materials = new Map(descriptor.materials.map(definition => {
        const material = new B.StandardMaterial('avatar-' + definition.id, scene);
        material.diffuseColor = new B.Color3(...definition.color);
        material.emissiveColor = new B.Color3(...definition.emissive);
        material.specularColor.set(definition.specular, definition.specular, definition.specular);
        return [definition.id, material];
    }));

    for (const group of descriptor.groups) {
        for (const part of group.parts) {
            const parent = jointFor(group.id, part.x) ?? root;
            const shape = createPart(part);
            shape.parent = parent;
            shape.position.set(part.x, part.baseY + part.height * part.scaleY / 2 - parent.position.y, part.z);
            shape.scaling.set(part.scaleX, part.scaleY, part.scaleZ);
            shape.material = materials.get(group.material);
            shape.isPickable = false;
        }
    }

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
        apply(x, z, yaw, stride, leftArm, rightArm) {
            root.position.set(x, 0, z);
            root.rotation.y = yaw;
            joints[0].rotation.x = stride;
            joints[1].rotation.x = -stride;
            joints[2].rotation.x = leftArm;
            joints[3].rotation.x = rightArm;
        }
    };

    function jointFor(group, x) {
        if (group === 'legs' || group === 'shoes') return joints[x < 0 ? 0 : 1];
        if (group === 'arm-left' || group === 'hand-left') return joints[2];
        if (group === 'arm-right' || group === 'hand-right') return joints[3];
        return undefined;
    }

    function createPart(part) {
        switch (part.shape) {
            case 'capsule':
                return B.MeshBuilder.CreateCapsule(part.id, { height: part.height, radius: part.diameterBottom / 2,
                    subdivisions: part.subdivisions, tessellation: 10 }, scene);
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
