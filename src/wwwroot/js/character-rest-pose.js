(function (root) {
    'use strict';

    // The Quaternius LayToIdle key provided the supine layout. These directions
    // fit the dressed Innerscene proportions to a level support plane. Actor
    // axes are +Y up, +Z toward the feet when lying and X across the shoulders.
    const segments = [
        ['spine_01', 'spine_02', 0, .12, -1],
        ['spine_03', 'neck_01', 0, .045, -1],
        ['neck_01', 'head', -.05, .16, -1],
        ['upperarm_l', 'lowerarm_l', -.36, -.195, .91],
        ['lowerarm_l', 'hand_l', -.18, -.09, .98],
        ['hand_l', 'middle_01_l', -.09, -.055, 1],
        ['upperarm_r', 'lowerarm_r', .32, -.205, .925],
        ['lowerarm_r', 'hand_r', .15, -.07, .99],
        ['hand_r', 'middle_01_r', .14, -.055, 1],
        ['thigh_l', 'calf_l', -.09, -.02, .996],
        ['calf_l', 'foot_l', -.07, -.03, .997],
        ['foot_l', 'ball_l', -.32, .72, .62],
        ['thigh_r', 'calf_r', .12, .045, .992],
        ['calf_r', 'foot_r', .04, -.105, .994],
        ['foot_r', 'ball_r', .38, .65, .658]
    ];

    function bind(BABYLON, skeleton, visual, actor) {
        const joint = name => skeleton.bones.find(bone => bone.name === name)?.getTransformNode();
        const chains = segments.map(([name, child, x, y, z]) =>
            ({ node: joint(name), child: joint(child), direction: new BABYLON.Vector3(x, y, z).normalize() }))
            .filter(link => link.node?.rotationQuaternion && link.child);
        const pelvis = joint('pelvis');
        const neck = joint('neck_01'), head = joint('head');
        const neckRest = neck.rotationQuaternion.clone();
        const hands = ['l', 'r'].map((side, index) => ({
            node: joint('hand_' + side),
            middle: joint('middle_01_' + side),
            index: joint('index_01_' + side),
            pinky: joint('pinky_01_' + side),
            // Multipliers orient the cross product toward the palm on each side.
            palmSign: index === 0 ? 1 : -1
        }));
        const point = new BABYLON.Vector3(), current = new BABYLON.Vector3(), target = new BABYLON.Vector3();
        const across = new BABYLON.Vector3(), normal = new BABYLON.Vector3(), up = BABYLON.Axis.Y;
        const turn = new BABYLON.Quaternion(), partial = new BABYLON.Quaternion();
        const identity = BABYLON.Quaternion.Identity();
        const rotation = new BABYLON.Matrix(), desired = new BABYLON.Matrix(), inverse = new BABYLON.Matrix();

        const headWorld = new BABYLON.Matrix();

        function rotate(node, worldRotation) {
            BABYLON.Matrix.FromQuaternionToRef(worldRotation, rotation);
            node.getWorldMatrix().multiplyToRef(rotation, desired);
            node.parent.computeWorldMatrix(true).invertToRef(inverse);
            desired.multiplyToRef(inverse, desired);
            desired.decompose(undefined, node.rotationQuaternion, undefined);
        }

        function relaxHand({ node, middle, index, pinky, palmSign }, weight) {
            node.computeWorldMatrix(true).getTranslationToRef(point);
            middle.computeWorldMatrix(true).getTranslationToRef(current).subtractInPlace(point).normalize();
            index.computeWorldMatrix(true).getTranslationToRef(across);
            pinky.computeWorldMatrix(true).getTranslationToRef(point);
            across.subtractInPlace(point);
            BABYLON.Vector3.CrossToRef(across, current, normal);
            normal.normalize().scaleInPlace(palmSign);
            // Roll around the length of the hand to put its back on the ground.
            target.copyFrom(up).subtractInPlace(point.copyFrom(current).scaleInPlace(current.y)).normalize();
            BABYLON.Vector3.CrossToRef(normal, target, across);
            const angle = Math.atan2(BABYLON.Vector3.Dot(across, current), BABYLON.Vector3.Dot(normal, target));
            BABYLON.Quaternion.RotationAxisToRef(current, angle * weight, turn);
            rotate(node, turn);
        }

        function apply(weight, support) {
            if (weight <= .001) return;
            const actorWorld = actor.getWorldMatrix();
            // Preserve the face-up head orientation while shaping the supporting neck.
            headWorld.copyFrom(head.computeWorldMatrix(true));
            for (const { node, child, direction } of chains) {
                node.computeWorldMatrix(true).getTranslationToRef(point);
                child.computeWorldMatrix(true).getTranslationToRef(current).subtractInPlace(point).normalize();
                BABYLON.Vector3.TransformNormalToRef(direction, actorWorld, target);
                BABYLON.Quaternion.FromUnitVectorsToRef(current, target.normalize(), turn);
                BABYLON.Quaternion.SlerpToRef(identity, turn, weight, partial);
                rotate(node, partial);
            }
            head.parent.computeWorldMatrix(true).invertToRef(inverse);
            headWorld.multiplyToRef(inverse, desired);
            desired.decompose(undefined, head.rotationQuaternion, undefined);
            head.computeWorldMatrix(true);
            BABYLON.Vector3.TransformNormalToRef(BABYLON.Axis.Z, actorWorld, target);
            BABYLON.Quaternion.RotationAxisToRef(target.normalize(), .12 * weight, turn);
            rotate(head, turn);
            for (const hand of hands) relaxHand(hand, weight);

            actorWorld.invertToRef(inverse);
            pelvis.computeWorldMatrix(true).getTranslationToRef(point);
            BABYLON.Vector3.TransformCoordinatesToRef(point, inverse, target);
            visual.position.z -= target.z * weight;
            // The 8.2 cm pelvis offset was calibrated against the back of the dressed mesh
            // at reference scale. Supporting the pelvis keeps a low hand from lifting the torso.
            visual.position.y += (support + .082 * visual.scaling.y - point.y) * weight;
        }

        return { apply, reset: () => neck.rotationQuaternion.copyFrom(neckRest) };
    }

    root.UltimaCharacterRestPose = { bind };
})(globalThis);
