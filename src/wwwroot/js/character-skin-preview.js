(function (root) {
    'use strict';

    function drivers(rig) {
        return [
            ['pelvis', rig.root], ['spine_01', rig.upper], ['spine_03', rig.chest], ['head', rig.head],
            ...rig.legs.flatMap((leg, index) => {
                const side = index === 0 ? 'l' : 'r';
                return [
                    ['clavicle_' + side, rig.clavicles[index]],
                    ['upperarm_' + side, rig.shoulders[index], index === 0 ? 0.5 : -0.5],
                    ['lowerarm_' + side, rig.elbows[index], index === 0 ? 0.5 : -0.5, 0.59],
                    ['hand_' + side, rig.wrists[index], index === 0 ? 0.5 : -0.5, 0.59],
                    ['thigh_' + side, leg.hip],
                    ['calf_' + side, leg.knee],
                    ['foot_' + side, leg.ankle]
                ];
            })
        ];
    }

    function bindFingers(BABYLON, skeleton, actor) {
        const digitCurls = [
            ['index', [.15, .25, .10]], ['middle', [.19, .29, .13]],
            ['ring', [.23, .34, .15]], ['pinky', [.28, .38, .17]], ['thumb', [.08, .13, .07]]
        ];
        const joint = name => skeleton.bones.find(bone => bone.name === name)?.getTransformNode();
        const position = node => node.computeWorldMatrix(true).getTranslation();
        const delta = new BABYLON.Quaternion();
        const hands = ['l', 'r'].map((side, index) => {
            const wrist = position(joint('hand_' + side));
            const middle = position(joint('middle_01_' + side)).subtract(wrist);
            const across = position(joint('index_01_' + side)).subtract(position(joint('pinky_01_' + side)));
            const palm = BABYLON.Vector3.Cross(across, middle).normalize();
            const inward = BABYLON.Vector3.TransformNormal(new BABYLON.Vector3(index === 0 ? 1 : -1, 0, 0), actor);
            if (BABYLON.Vector3.Dot(palm, inward) < 0) palm.negateInPlace();
            const center = position(joint('middle_02_' + side)).subtract(position(joint('middle_01_' + side)));
            const planar = direction => direction.subtract(palm.scale(BABYLON.Vector3.Dot(direction, palm))).normalize();
            const forward = planar(center);
            return digitCurls.flatMap(([digit, curls]) => {
                const chain = curls.map((_, segment) => joint(digit + '_0' + (segment + 1) + '_' + side));
                return chain.map((node, segment) => {
                    const next = Math.min(segment + 1, 2), previous = Math.min(segment, 1);
                    const along = position(chain[next]).subtract(position(chain[previous]));
                    const inverse = node.computeWorldMatrix(true).clone().invert();
                    // Compute the curl axis in bone space, including the imported mirror transform.
                    const axis = BABYLON.Vector3.Cross(BABYLON.Vector3.TransformNormal(along, inverse),
                        BABYLON.Vector3.TransformNormal(palm, inverse)).normalize();
                    const rest = node.rotationQuaternion.clone();
                    if (segment === 0 && digit !== 'thumb') {
                        // Bring the fingers toward the middle finger within the palm plane.
                        const direction = planar(along);
                        const separation = Math.atan2(BABYLON.Vector3.Dot(palm, BABYLON.Vector3.Cross(direction, forward)),
                            BABYLON.Vector3.Dot(direction, forward));
                        const localPalm = BABYLON.Vector3.TransformNormal(palm, inverse).normalize();
                        const handedness = inverse.determinant() < 0 ? -1 : 1;
                        BABYLON.Quaternion.RotationAxisToRef(localPalm,
                            handedness * Math.max(-.28, Math.min(.28, separation * .80)), delta);
                        rest.multiplyInPlace(delta);
                    }
                    return { node, axis, rest, curl: curls[segment], channel: index * 3 + segment,
                        response: digit === 'thumb' ? .38 : 1 };
                });
            });
        });
        return angles => {
            for (const hand of hands) {
                for (const { node, axis, rest, curl, channel, response } of hand) {
                    BABYLON.Quaternion.RotationAxisToRef(axis, curl + angles[channel] * response, delta);
                    rest.multiplyToRef(delta, node.rotationQuaternion);
                }
            }
        };
    }

    function fitLegs(BABYLON, rig, visual, targets, solve) {
        const point = new BABYLON.Vector3(), from = new BABYLON.Vector3(), to = new BABYLON.Vector3(), start = new BABYLON.Vector3();
        const delta = new BABYLON.Quaternion();
        const rotation = new BABYLON.Matrix(), desired = new BABYLON.Matrix(), inverseParent = new BABYLON.Matrix();
        const input = new Float64Array(30);
        const legs = rig.legs.map((leg, index) => {
            const side = index === 0 ? 'l' : 'r';
            const find = prefix => targets.find(binding => binding.name === prefix + side);
            const foot = find('foot_');
            // Both neutral transforms are measured with the actor upright. Keep the imported sole height.
            const rise = foot.rest.getTranslationToRef(point).y - foot.driverRestY;
            return { hip: find('thigh_').target, knee: find('calf_').target, ankle: foot.target,
                driver: leg.ankle, rise, sole: new BABYLON.Matrix() };
        });

        function orient(node, world) {
            node.parent.computeWorldMatrix(true).invertToRef(inverseParent);
            world.multiplyToRef(inverseParent, desired);
            desired.decompose(undefined, node.rotationQuaternion, undefined);
        }

        function aim(node, child, target) {
            node.computeWorldMatrix(true).getTranslationToRef(start);
            child.computeWorldMatrix(true).getTranslationToRef(from).subtractInPlace(start).normalize();
            to.copyFrom(target).subtractInPlace(start).normalize();
            BABYLON.Quaternion.FromUnitVectorsToRef(from, to, delta);
            BABYLON.Matrix.FromQuaternionToRef(delta, rotation);
            node.getWorldMatrix().multiplyToRef(rotation, desired);
            orient(node, desired);
        }

        return function alignFeet(support, delta) {
            const yaw = rig.root.rotation.y;
            for (let index = 0; index < legs.length; index++) {
                const { hip, knee, ankle, driver, rise, sole } = legs[index];
                const offset = index * 15;
                hip.computeWorldMatrix(true).getTranslationToRef(point).toArray(input, offset);
                knee.computeWorldMatrix(true).getTranslationToRef(point).toArray(input, offset + 3);
                sole.copyFrom(ankle.computeWorldMatrix(true)).getTranslationToRef(point).toArray(input, offset + 6);
                driver.computeWorldMatrix(true).getTranslationToRef(point);
                point.y += rise;
                point.toArray(input, offset + 9);
                point.set(Math.sin(yaw), 0, Math.cos(yaw)).toArray(input, offset + 12);
            }
            const output = solve(input, rig.root.position.y, support, rig.gaitWeight, delta);
            visual.position.y += output[12];
            for (let index = 0; index < legs.length; index++) {
                const { hip, knee, ankle, sole } = legs[index];
                aim(hip, knee, point.fromArray(output, index * 6));
                aim(knee, ankle, point.fromArray(output, index * 6 + 3));
                orient(ankle, sole);
            }
        };
    }

    function prepare(BABYLON, rig, solveLegs) {
        const actorInverse = new BABYLON.Matrix();
        const actorLocal = new BABYLON.Matrix();
        const transform = new BABYLON.Matrix();
        const desired = new BABYLON.Matrix();
        const parentInverse = new BABYLON.Matrix();
        const position = new BABYLON.Vector3();
        rig.root.computeWorldMatrix(true).invertToRef(actorInverse);
        const driverRest = drivers(rig).map(([name, node, armRest, forearmRest]) => {
            node.computeWorldMatrix(true).multiplyToRef(actorInverse, actorLocal);
            return { name, node, armRest, forearmRest, driverRestY: actorLocal.getTranslationToRef(position).y,
                inverseRest: actorLocal.invertToRef(new BABYLON.Matrix()) };
        });

        return function bind(container, shadows, layer) {
            const skeleton = container.skeletons[0];
            if (container.skeletons.length !== 1 || skeleton.bones.length !== 53) {
                throw new Error('Unexpected Innerscene skeleton');
            }

            const targets = driverRest.map(binding => {
                const target = skeleton.bones.find(bone => bone.name === binding.name)?.getTransformNode();
                if (!target?.rotationQuaternion) throw new Error('Missing skin joint: ' + binding.name);
                return { ...binding, target };
            });
            const importedRoot = container.rootNodes[0];
            const visual = new BABYLON.TransformNode('innerscene-player-preview', rig.root.getScene());
            visual.parent = rig.root;
            visual.scaling.setAll(rig.body.height / 1.6803);
            visual.position.y = -rig.body.floorOffset;
            importedRoot.parent = visual;
            rig.root.computeWorldMatrix(true).invertToRef(actorInverse);
            for (const binding of targets) {
                binding.rest = binding.target.computeWorldMatrix(true).multiply(actorInverse);
                // Lower each arm from the source A-pose around actor Z before applying gait deltas.
                if (binding.armRest) binding.rest.multiplyToRef(BABYLON.Matrix.RotationZ(binding.armRest), binding.rest);
                // The source forearms start flexed. Keep a slight bend when the hands hang at rest.
                if (binding.forearmRest) binding.rest.multiplyToRef(BABYLON.Matrix.RotationX(binding.forearmRest), binding.rest);
            }
            const curlFingers = bindFingers(BABYLON, skeleton, rig.root.getWorldMatrix());
            const alignFeet = fitLegs(BABYLON, rig, visual, targets, solveLegs);

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
            return function update(support, delta) {
                visual.position.y = -rig.body.floorOffset;
                visual.position.x = rig.skinMotion.sway;
                const actorWorld = rig.root.computeWorldMatrix(true);
                actorWorld.invertToRef(actorInverse);
                for (const { name, node, target, inverseRest, rest } of targets) {
                    // Work in actor space to carry source bind orientations, including the glTF handedness conversion.
                    if (name === 'pelvis') {
                        // Apply the visual pelvis before the leg solve. The gameplay rig's foot targets stay in place.
                        BABYLON.Matrix.RotationYawPitchRollToRef(rig.skinMotion.yaw, 0, rig.skinMotion.roll, actorLocal);
                    } else {
                        node.computeWorldMatrix(true).multiplyToRef(actorInverse, actorLocal);
                    }
                    inverseRest.multiplyToRef(actorLocal, transform);
                    rest.multiplyToRef(transform, desired);
                    target.parent.computeWorldMatrix(true).invertToRef(parentInverse);
                    desired.multiplyToRef(actorWorld, transform);
                    transform.multiplyToRef(parentInverse, desired);
                    // Preserve bind translations and scale. C# drives the rotation of each mapped joint.
                    desired.decompose(undefined, target.rotationQuaternion, undefined);
                }
                curlFingers(rig.skinMotion.fingerAngles);
                alignFeet(support, delta);
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
