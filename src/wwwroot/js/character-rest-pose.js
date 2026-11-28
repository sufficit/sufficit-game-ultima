(function (root) {
    'use strict';

    // Quaternius UAL2 LayToIdle, first key. Unit directions use Ultima's actor axes:
    // +Y up, +Z forward, with source glTF X reflected for Babylon's left-handed scene.
    // Retarget directions rather than translations to preserve every Innerscene bone length.
    const segments = [
        ['spine_01', 'spine_02', 0, -.0304, -.9995],
        ['spine_03', 'neck_01', .0008, .2415, -.9704],
        ['upperarm_l', 'lowerarm_l', -.2066, -.0299, .9780],
        ['lowerarm_l', 'hand_l', -.2060, .0079, .9785],
        ['upperarm_r', 'lowerarm_r', .2270, .0192, .9737],
        ['lowerarm_r', 'hand_r', .2271, .0198, .9737],
        ['thigh_l', 'calf_l', -.0946, -.0621, .9936],
        ['calf_l', 'foot_l', -.0926, -.0669, .9934],
        ['foot_l', 'ball_l', -.2681, .4009, .8760],
        ['thigh_r', 'calf_r', .1042, .1038, .9891],
        ['calf_r', 'foot_r', -.0906, -.1674, .9817],
        ['foot_r', 'ball_r', .1150, .5221, .8451]
    ];

    function bind(BABYLON, skeleton, visual, actor) {
        const joint = name => skeleton.bones.find(bone => bone.name === name)?.getTransformNode();
        const chains = segments.map(([name, child, x, y, z]) =>
            ({ node: joint(name), child: joint(child), direction: new BABYLON.Vector3(x, y, z).normalize() }))
            .filter(link => link.node?.rotationQuaternion && link.child);
        // Approximate the back, skull, sleeves and boots using joint-to-surface distances
        // measured in metres on the 1.6803 m dressed reference. Scale those with the mesh.
        const pelvis = joint('pelvis');
        const contacts = [
            ['pelvis', .105], ['spine_03', .092], ['head', .072],
            ['lowerarm_l', .042], ['lowerarm_r', .042], ['hand_l', .027], ['hand_r', .027],
            ['calf_l', .061], ['calf_r', .061], ['foot_l', .065], ['foot_r', .065]
        ].map(([name, depth]) => ({ node: joint(name), depth })).filter(contact => contact.node);
        const point = new BABYLON.Vector3(), current = new BABYLON.Vector3(), target = new BABYLON.Vector3();
        const turn = new BABYLON.Quaternion(), partial = new BABYLON.Quaternion();
        const identity = BABYLON.Quaternion.Identity();
        const rotation = new BABYLON.Matrix(), desired = new BABYLON.Matrix(), inverse = new BABYLON.Matrix();

        return (weight, support) => {
            if (weight <= .001) return;
            const actorWorld = actor.getWorldMatrix();
            for (const { node, child, direction } of chains) {
                node.computeWorldMatrix(true).getTranslationToRef(point);
                child.computeWorldMatrix(true).getTranslationToRef(current).subtractInPlace(point).normalize();
                BABYLON.Vector3.TransformNormalToRef(direction, actorWorld, target);
                BABYLON.Quaternion.FromUnitVectorsToRef(current, target.normalize(), turn);
                BABYLON.Quaternion.SlerpToRef(identity, turn, weight, partial);
                BABYLON.Matrix.FromQuaternionToRef(partial, rotation);
                node.getWorldMatrix().multiplyToRef(rotation, desired);
                node.parent.computeWorldMatrix(true).invertToRef(inverse);
                desired.multiplyToRef(inverse, desired);
                desired.decompose(undefined, node.rotationQuaternion, undefined);
            }
            if (pelvis) {
                // Keep the pelvis centered over the player when the visual rotates around its original foot pivot.
                actorWorld.invertToRef(inverse);
                pelvis.computeWorldMatrix(true).getTranslationToRef(point);
                BABYLON.Vector3.TransformCoordinatesToRef(point, inverse, target);
                visual.position.z -= target.z * weight;
            }
            let lowest = Infinity;
            for (const { node, depth } of contacts) {
                node.computeWorldMatrix(true).getTranslationToRef(point);
                lowest = Math.min(lowest, point.y - depth * visual.scaling.y);
            }
            // Support the silhouette without evaluating the full skinned mesh on the CPU.
            if (Number.isFinite(lowest)) visual.position.y += (support - lowest) * weight;
        };
    }

    root.UltimaCharacterRestPose = { bind };
})(globalThis);
