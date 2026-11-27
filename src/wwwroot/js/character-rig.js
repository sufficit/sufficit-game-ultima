(function (root) {
    'use strict';
    // Legacy dimensions are used by the renderer when a body packet is unavailable.
    const PLAYER_HEIGHT = 1.75;
    // MPFB2 fallback for avatars without the C# anatomy packet.
    // Matches HumanoidBody.DefaultShoulderHeightRatio. The lateral value below matches ShoulderOffsetRatio.
    const SHOULDER_RATIO = 0.80518;
    const HIP_HEIGHT = PLAYER_HEIGHT * .44;
    const LEG_RADIUS = PLAYER_HEIGHT * .039;
    const ANKLE_HEIGHT = LEG_RADIUS * 1.25 * .55;
    const LEG_SEGMENT = (HIP_HEIGHT - ANKLE_HEIGHT + .004) / 2;
    const LEG_SPACING = PLAYER_HEIGHT * .052;
    const SHOE_OFFSET = .9 * LEG_RADIUS;

    function createRenderer(BABYLON, scene, track) {
        function createCharacterJoint(root, name, parent, x, y, z = 0) {
            const node = new BABYLON.TransformNode(root.name + '-' + name, scene);
            node.parent = parent;
            const offset = parent.metadata?.modelOffset;
            node.position.set(x + (offset?.x ?? 0), y + (offset?.y ?? 0), z + (offset?.z ?? 0));
            node.metadata = { modelOffset: new BABYLON.Vector3(-x, -y, -z) };
            return node;
        }

        function createJointHierarchy(root, anatomy) {
            // The C# packet provides ordered model-space pivots. Babylon nodes store parent-relative transforms.
            const joints = new Map([['root', root]]);
            for (const { name, parent, x, y, z, motion, flexion } of anatomy) {
                const node = createCharacterJoint(root, name, joints.get(parent), x, y, z);
                node.metadata.motion = motion;
                if (flexion) node.metadata.flexion = flexion;
                joints.set(name, node);
            }
            return joints;
        }

        let faceInk;
        function createCharacterRig(root, guardian, castsShadow, anatomy, body) {
            const joints = anatomy ? createJointHierarchy(root, anatomy) : null;
            const headRadius = guardian ? 1.7 * 0.075 : (body?.height ?? PLAYER_HEIGHT) * 0.07;
            const headBase = guardian ? 1.7 * 0.82 : (body?.height ?? PLAYER_HEIGHT) - 2 * headRadius;
            const upper = joints?.get('upper-body') ?? createCharacterJoint(root, 'upper-body', root, 0, guardian ? 1.7 * 0.52 : HIP_HEIGHT);
            const head = joints?.get('head-pivot') ?? createCharacterJoint(root, 'head-pivot', upper, 0, headBase);
            const eyes = joints?.get('eye-pivot') ?? createCharacterJoint(root, 'eye-pivot', head, 0, headBase + 1.1 * headRadius);
            eyes.setEnabled(false);
            const armX = PLAYER_HEIGHT * 0.10067;
            const shoulders = guardian ? [] : [-1, 1].map(side =>
                joints?.get(side < 0 ? 'shoulder-left' : 'shoulder-right') ??
                    createCharacterJoint(root, side < 0 ? 'shoulder-left' : 'shoulder-right', upper, side * armX, PLAYER_HEIGHT * SHOULDER_RATIO));
            const elbows = shoulders.map((shoulder, i) => {
                const side = i === 0 ? 'left' : 'right';
                return joints?.get('elbow-' + side) ?? createCharacterJoint(root, 'elbow-' + side,
                    shoulder, (i * 2 - 1) * armX, PLAYER_HEIGHT * (SHOULDER_RATIO - 0.17));
            });
            const wrists = elbows.map((elbow, i) => {
                const side = i === 0 ? 'left' : 'right';
                return joints?.get('wrist-' + side) ?? createCharacterJoint(root, 'wrist-' + side,
                    elbow, (i * 2 - 1) * armX, PLAYER_HEIGHT * (SHOULDER_RATIO - 0.35));
            });
            if (shoulders.length) {
                shoulders[0].rotation.z = -0.055;
                shoulders[1].rotation.z = 0.055;
            }
            const face = new BABYLON.TransformNode(root.name + '-expression', scene);
            face.parent = head;
            face.setEnabled(false);
            if (!faceInk) {
                faceInk = new BABYLON.StandardMaterial('character-face-ink', scene);
                faceInk.diffuseColor.set(0.10, 0.045, 0.028);
                faceInk.emissiveColor.set(0.035, 0.014, 0.009);
            }
            function facialFeature(name, parent, x, y, z, width, height) {
                const feature = BABYLON.MeshBuilder.CreateSphere(root.name + '-' + name, { diameter: 1, segments: 5 }, scene);
                feature.parent = parent;
                feature.position.set(x, y, z);
                feature.scaling.set(width, height, 0.012);
                feature.material = faceInk;
                feature.isPickable = false;
                return feature;
            }
            if (guardian) {
                for (const side of [-1, 1]) {
                    facialFeature('eye-' + side, eyes, side * headRadius * 0.36, 0, headRadius * 0.94, 0.023, 0.018);
                }
            }
            const brows = [-1, 1].map(side => facialFeature('brow-' + side, face,
                side * headRadius * 0.36 * (body?.faceWidth ?? 1), headRadius * 1.34, headRadius * 0.9, 0.038, 0.009));
            const mouth = facialFeature('mouth', face, 0, headRadius * 0.68, headRadius * 0.96, 0.048, 0.008);
            const attachments = {
                torso: upper, shoulders: upper, tunic: upper, neck: upper, head, ears: head, hair: head, hood: head, skin: head, eyes,
                'arm-left': shoulders[0], 'hand-left': wrists[0],
                'arm-right': shoulders[1], 'hand-right': wrists[1]
            };
            const legs = guardian ? [] : buildCharacterLegs(root, joints, body);
            const rig = {
                root, upper, head, eyes, shoulders, elbows, wrists, face, brows, mouth, attachments, castsShadow, shadowMeshes: [],
                limbMeshes: [[], [], [], [], []],
                body, legs, fallbackLegMaterials: legs.length ? [legs[0].thigh.material, legs[0].shoe.material] : [],
                clock: 0, gaitPhase: 0, gaitWeight: 0, speaking: 0, action: null
            };
            for (const [index, { thigh, shin, shoe }] of legs.entries()) {
                if (joints) {
                    const limb = 3 + index;
                    for (const mesh of [thigh, shin, shoe]) mesh.metadata = { contactLimb: limb };
                    rig.limbMeshes[limb].push(thigh, shin, shoe);
                }
                track(rig, thigh);
                track(rig, shin);
                track(rig, shoe);
            }
            return rig;
        }

        function buildCharacterLegs(root, joints, body) {
            const pants = new BABYLON.StandardMaterial(root.name + '-leg-pants', scene);
            pants.diffuseColor.set(0.20, 0.22, 0.28);
            const shoes = new BABYLON.StandardMaterial(root.name + '-leg-shoes', scene);
            shoes.diffuseColor.set(0.13, 0.10, 0.09);
            const radius = body?.legRadius ?? LEG_RADIUS;
            const femur = body?.femurLength ?? LEG_SEGMENT;
            const tibia = body?.tibiaLength ?? LEG_SEGMENT;
            return [-1, 1].map(side => {
                const suffix = side < 0 ? 'left' : 'right';
                let hip = joints?.get('hip-' + suffix);
                let knee = joints?.get('knee-' + suffix);
                let ankle = joints?.get('ankle-' + suffix);
                if (!joints) {
                    hip = new BABYLON.TransformNode(root.name + '-hip-' + suffix, scene);
                    hip.parent = root;
                    hip.position.set(side * LEG_SPACING, HIP_HEIGHT, 0);
                    knee = new BABYLON.TransformNode(root.name + '-knee-' + suffix, scene);
                    knee.parent = hip;
                    knee.position.y = -LEG_SEGMENT;
                    ankle = new BABYLON.TransformNode(root.name + '-ankle-' + suffix, scene);
                    ankle.parent = knee;
                    ankle.position.y = -LEG_SEGMENT;
                }

                let hipRoll = null;
                if (joints) {
                    hipRoll = new BABYLON.TransformNode(root.name + '-hip-roll-' + suffix, scene);
                    hipRoll.parent = hip.parent;
                    hipRoll.position.copyFrom(hip.position);
                    hip.parent = hipRoll;
                    hip.position.set(0, 0, 0);
                    hip.rotationQuaternion = BABYLON.Quaternion.Identity();
                    ankle.rotationQuaternion = BABYLON.Quaternion.Identity();
                }
                const thigh = BABYLON.MeshBuilder.CreateCapsule(root.name + '-thigh-' + suffix,
                    { height: femur + 0.085, radius: body?.thighRadius ?? radius, subdivisions: 4 }, scene);
                const shin = BABYLON.MeshBuilder.CreateCapsule(root.name + '-shin-' + suffix,
                    { height: tibia + 0.085, radius: body?.calfRadius ?? radius, subdivisions: 4 }, scene);
                thigh.parent = hip;
                shin.parent = knee;
                thigh.position.y = -femur / 2;
                shin.position.y = -tibia / 2;
                thigh.material = shin.material = pants;
                const shoe = BABYLON.MeshBuilder.CreateSphere(root.name + '-shoe-' + suffix,
                    { diameter: 2.5 * radius, segments: 8 }, scene);
                shoe.parent = ankle;
                shoe.position.z = body?.shoeOffset ?? SHOE_OFFSET;
                shoe.scaling.set(1, 0.55, 1.9);
                shoe.material = shoes;
                for (const mesh of [thigh, shin, shoe]) {
                    mesh.isPickable = false;
                }
                return { side, hip, hipRoll, knee, ankle, thigh, shin, shoe, femur, tibia, stride: new Float64Array(6) };
            });
        }

        const undoLegPitch = new BABYLON.Quaternion(), undoHipRoll = new BABYLON.Quaternion();
        const soleRotation = new BABYLON.Quaternion(), ankleBase = new BABYLON.Quaternion();

        const hipPitch = new BABYLON.Quaternion(), kneeTwist = new BABYLON.Quaternion();
        const undoKneeTwist = new BABYLON.Quaternion(), undoLegAxes = new BABYLON.Quaternion();
        const legAxis = new BABYLON.Vector3();

        function applyLeg(leg, angles, i, roll, yaw, swivel) {
            const hip = angles[i], knee = angles[i + 1], ankle = angles[i + 2];
            leg.knee.rotation.x = knee;
            if (!leg.hipRoll) {
                leg.hip.rotation.x = hip;
                leg.ankle.rotation.x = ankle;
                return;
            }
            leg.hipRoll.rotation.z = roll;
            BABYLON.Quaternion.RotationYawPitchRollToRef(0, hip, 0, hipPitch);
            BABYLON.Quaternion.RotationYawPitchRollToRef(0, 0, -roll, undoHipRoll);
            let undoAxes = undoHipRoll;
            if (swivel) {
                // Twist about the hip-to-ankle vector so the foot stays planted.
                const axisPitch = hip + (leg.femur === leg.tibia ? knee * 0.5 :
                    Math.atan2(leg.tibia * Math.sin(knee), leg.femur + leg.tibia * Math.cos(knee)));
                legAxis.set(0, Math.cos(axisPitch), Math.sin(axisPitch));
                BABYLON.Quaternion.RotationAxisToRef(legAxis, swivel, kneeTwist);
                kneeTwist.multiplyToRef(hipPitch, leg.hip.rotationQuaternion);
                undoKneeTwist.set(-kneeTwist.x, -kneeTwist.y, -kneeTwist.z, kneeTwist.w);
                undoKneeTwist.multiplyToRef(undoHipRoll, undoLegAxes);
                undoAxes = undoLegAxes;
            } else {
                leg.hip.rotationQuaternion.copyFrom(hipPitch);
            }
            // Cancel leg rotation before aligning the sole with its contact heading.
            BABYLON.Quaternion.RotationYawPitchRollToRef(0, -hip - knee, 0, undoLegPitch);
            BABYLON.Quaternion.RotationYawPitchRollToRef(yaw, hip + knee + ankle, 0, soleRotation);
            undoLegPitch.multiplyToRef(undoAxes, ankleBase);
            ankleBase.multiplyToRef(soleRotation, leg.ankle.rotationQuaternion);
        }

        function applyLegs(rig, frame, angles, i, isPlayer, offset = 0) {
            const [left, right] = rig.legs;
            rig.root.position.y = angles[i];
            if (isPlayer) {
                left.stride[0] = frame[offset + 28]; right.stride[0] = frame[offset + 33];
                left.stride[1] = frame[offset + 41]; right.stride[1] = frame[offset + 42];
                left.stride[2] = frame[offset + 29]; right.stride[2] = frame[offset + 34];
                left.stride[3] = frame[offset + 35]; right.stride[3] = frame[offset + 36];
                left.stride[4] = frame[offset + 39]; right.stride[4] = frame[offset + 40];
                left.stride[5] = frame[offset + 43]; right.stride[5] = frame[offset + 44];
            }
            applyLeg(left, angles, i + 1, i === 0 ? angles[7] : frame[offset + 37], frame[offset + 39], frame[offset + 43]);
            applyLeg(right, angles, i + 4, i === 0 ? angles[8] : frame[offset + 38], frame[offset + 40], frame[offset + 44]);
        }

        function applyUpperBody(rig, frame, isPlayer, offset = 0) {
            const torsoX = frame[offset + 2], torsoZ = frame[offset + 3], headX = frame[offset + 4], headZ = frame[offset + 5];
            const eyesY = frame[offset + 6], mouthX = frame[offset + 7], mouthY = frame[offset + 8], browZ = frame[offset + 9];
            const leftArm = frame[offset + 10], rightArm = frame[offset + 11];
            rig.upper.rotation.x = torsoX;
            rig.upper.rotation.z = torsoZ;
            if (isPlayer) { rig.upper.rotation.y = frame[offset + 45]; }
            rig.head.rotation.x = headX;
            rig.head.rotation.z = headZ;
            rig.eyes.scaling.y = eyesY;
            rig.mouth.scaling.y = mouthY;
            rig.mouth.scaling.x = mouthX;
            rig.brows[0].rotation.z = browZ;
            rig.brows[1].rotation.z = -browZ;
            if (rig.shoulders.length) {
                rig.elbows[0].rotation.x = rig.elbows[1].rotation.x = 0;
                rig.shoulders[0].rotation.x = leftArm;
                rig.shoulders[1].rotation.x = rightArm;
            }
        }

        function mount(rig, modelId, protos) {
            rig.face.setEnabled(true);
            rig.eyes.setEnabled(true);
            for (const proto of protos) {
                const group = proto.name.slice(('model-' + modelId + '-').length);
                if (rig.legs.length && group === 'legs') {
                    // Articulated capsules already use the body measurements. Reuse the signed material.
                    for (const leg of rig.legs) { leg.thigh.material = leg.shin.material = proto.material; }
                    continue;
                }
                if (rig.legs.length && group === 'shoes') {
                    for (const leg of rig.legs) { leg.shoe.material = proto.material; }
                    continue;
                }
                if (rig.elbows.length && (group === 'arm-left' || group === 'arm-right')) {
                    const side = group === 'arm-left' ? 0 : 1;
                    const name = proto.name.replace('model-', 'char-') + '-' + rig.root.name;
                    const radius = PLAYER_HEIGHT * 0.026;
                    const segments = [
                        [name, rig.shoulders[side], -rig.elbows[side].position.y, rig.body?.upperArmRadius ?? radius],
                        [name + '-forearm', rig.elbows[side], -rig.wrists[side].position.y, rig.body?.forearmRadius ?? radius]
                    ];
                    for (const [meshName, parent, length, radius] of segments) {
                        const mesh = BABYLON.MeshBuilder.CreateCapsule(meshName,
                            { height: length + radius, radius, subdivisions: 2, tessellation: 8 }, scene);
                        mesh.parent = parent;
                        mesh.position.y = -length / 2;
                        mesh.material = proto.material;
                        mesh.isPickable = false;
                        mesh.metadata = { contactLimb: side + 1 };
                        rig.limbMeshes[side + 1].push(mesh);
                        track(rig, mesh);
                    }
                    continue;
                }
                const clone = proto.clone(proto.name.replace('model-', 'char-') + '-' + rig.root.name);
                clone.isVisible = true;
                clone.isPickable = false;
                const pivot = rig.attachments[group] || rig.root;
                clone.parent = pivot;
                clone.position.set(0, 0, 0);
                if (pivot !== rig.root) { clone.position.copyFrom(pivot.metadata.modelOffset); }
                if (rig.elbows.length && (group === 'hand-left' || group === 'hand-right')) {
                    const limb = group === 'hand-left' ? 1 : 2;
                    clone.metadata = { contactLimb: limb };
                    rig.limbMeshes[limb].push(clone);
                }
                track(rig, clone);
            }
            for (const material of rig.fallbackLegMaterials.splice(0)) { material.dispose(); }
        }

        return { create: createCharacterRig, mount, applyLeg, applyLegs, applyUpperBody };
    }
    root.UltimaCharacterRig = { createRenderer };
})(globalThis);
