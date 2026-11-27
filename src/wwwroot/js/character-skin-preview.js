(function (root) {
    'use strict';

    function drivers(rig) {
        return [
            ['spine_01', rig.upper], ['head', rig.head],
            ...rig.legs.flatMap((leg, index) => {
                const side = index === 0 ? 'l' : 'r';
                return [
                    ['upperarm_' + side, rig.shoulders[index], index === 0 ? 0.5 : -0.5],
                    ['lowerarm_' + side, rig.elbows[index], index === 0 ? 0.5 : -0.5],
                    ['hand_' + side, rig.wrists[index], index === 0 ? 0.5 : -0.5],
                    ['thigh_' + side, leg.hip],
                    ['calf_' + side, leg.knee],
                    ['foot_' + side, leg.ankle]
                ];
            })
        ];
    }

    function prepare(BABYLON, rig) {
        const actorInverse = new BABYLON.Matrix();
        const actorLocal = new BABYLON.Matrix();
        const transform = new BABYLON.Matrix();
        const desired = new BABYLON.Matrix();
        const parentInverse = new BABYLON.Matrix();
        rig.root.computeWorldMatrix(true).invertToRef(actorInverse);
        const driverRest = drivers(rig).map(([name, node, armRest]) => {
            node.computeWorldMatrix(true).multiplyToRef(actorInverse, actorLocal);
            return { name, node, armRest, inverseRest: actorLocal.invertToRef(new BABYLON.Matrix()) };
        });

        return function bind(container, shadows, layer) {
            const skeleton = container.skeletons[0];
            if (container.skeletons.length !== 1 || skeleton.bones.length !== 53) {
                throw new Error('Unexpected Innerscene skeleton');
            }

            const targets = driverRest.map(({ name, node, inverseRest, armRest }) => {
                const target = skeleton.bones.find(bone => bone.name === name)?.getTransformNode();
                if (!target?.rotationQuaternion) throw new Error('Missing skin joint: ' + name);
                return { node, target, inverseRest, armRest };
            });
            const importedRoot = container.rootNodes[0];
            const visual = new BABYLON.TransformNode('innerscene-player-preview', rig.root.getScene());
            visual.parent = rig.root;
            visual.scaling.setAll(rig.body.height / 1.6803);
            importedRoot.parent = visual;
            rig.root.computeWorldMatrix(true).invertToRef(actorInverse);
            for (const binding of targets) {
                binding.rest = binding.target.computeWorldMatrix(true).multiply(actorInverse);
                // Lower each arm from the source A-pose around actor Z before applying gait deltas.
                if (binding.armRest) binding.rest.multiplyToRef(BABYLON.Matrix.RotationZ(binding.armRest), binding.rest);
            }

            // Let Babylon deform the display mesh. The procedural rig still provides all contacts and hitboxes.
            for (const mesh of container.meshes) {
                mesh.layerMask = layer;
                mesh.isPickable = false;
                if (mesh.skeleton && rig.castsShadow) shadows?.addShadowCaster(mesh);
            }
            container.addAllToScene();
            for (const mesh of rig.shadowMeshes) {
                mesh.visibility = 0;
                shadows?.removeShadowCaster(mesh);
            }
            return function update() {
                const actorWorld = rig.root.computeWorldMatrix(true);
                actorWorld.invertToRef(actorInverse);
                for (const { node, target, inverseRest, rest } of targets) {
                    // Work in actor space to carry source bind orientations, including the glTF handedness conversion.
                    node.computeWorldMatrix(true).multiplyToRef(actorInverse, actorLocal);
                    inverseRest.multiplyToRef(actorLocal, transform);
                    rest.multiplyToRef(transform, desired);
                    target.parent.computeWorldMatrix(true).invertToRef(parentInverse);
                    desired.multiplyToRef(actorWorld, transform);
                    transform.multiplyToRef(parentInverse, desired);
                    // Preserve bind translations and scale. C# drives the rotation of each mapped joint.
                    desired.decompose(undefined, target.rotationQuaternion, undefined);
                }
            };
        };
    }

    async function load(BABYLON, scene, bytes, bind) {
        const url = URL.createObjectURL(new Blob([bytes], { type: 'model/gltf-binary' }));
        let container;
        try {
            container = await BABYLON.SceneLoader.LoadAssetContainerAsync(url, '', scene, undefined, '.glb');
            return bind(container);
        } catch (error) {
            container?.dispose();
            throw error;
        } finally {
            URL.revokeObjectURL(url);
        }
    }

    root.UltimaCharacterSkinPreview = { prepare, load };
})(globalThis);
