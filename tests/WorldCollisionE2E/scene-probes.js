// Executado dentro da página ou do worker. As condições do teste ficam no runner C#.
(() => {
    const scene = BABYLON.EngineStore.LastCreatedScene;
    const node = name => scene.getTransformNodeByName(name);
    const mesh = name => scene.getMeshByName(name);
    const player = node('player');
    const engine = scene.getEngine();
    const getDeltaTime = engine.getDeltaTime;
    const torso = node('player-upper-body');
    const sides = ['left', 'right'];
    const ankles = sides.map(side => node('player-ankle-' + side));
    const knees = sides.map(side => node('player-knee-' + side));
    const clock = () => Math.min(0.05, scene.getEngine().getDeltaTime() / 1000);
    const xyz = ({x, y, z}) => ({x, y, z});
    const position = () => xyz(player.position);
    const nextFrame = () => new Promise(resolve => scene.onAfterRenderObservable.addOnce(resolve));
    const box = object => object.getBoundingInfo().boundingBox;
    const deltaPose = () => ({...position(), g: clock(), yaw: player.rotation.y});
    let observer;
    let samples;

    function sampleUntil(timeout, label, capture) {
        return new Promise((resolve, reject) => {
            const frame = scene.onAfterRenderObservable.add(() => {
                const sample = capture();
                if (sample === undefined) return;
                clearTimeout(timer);
                scene.onAfterRenderObservable.remove(frame);
                resolve(sample);
            });
            const timer = setTimeout(() => {
                scene.onAfterRenderObservable.remove(frame);
                reject(Error(label));
            }, timeout);
        });
    }

    function armNodes() {
        return {
            armA: mesh('char-character.villager-arm-right-player'),
            armB: mesh('char-character.wanderer-arm-left-remote-pose-arm'),
            remote: node('remote-pose-arm'),
            shoulderA: node('player-shoulder-right'),
            shoulderB: node('remote-pose-arm-shoulder-left')
        };
    }

    function armPose() {
        const {armA, armB, remote} = armNodes();
        return {
            overlap: armA.intersectsMesh(armB, true),
            player: position(),
            remote: xyz(remote.position),
            leftBend: node('remote-pose-arm-elbow-left').rotation.x,
            rightBend: node('player-elbow-right').rotation.x
        };
    }

    function boundsGap(remote, axis, visibleOnly) {
        const meshes = root => root.getChildMeshes().filter(m => !visibleOnly || m.isEnabled() && m.isVisible);
        const front = Math.max(...meshes(player).map(m => box(m).maximumWorld[axis]));
        const back = Math.min(...meshes(node(remote)).map(m => box(m).minimumWorld[axis]));
        return back - front;
    }

    const capture = {
        gait() {
            return {
                ...deltaPose(), torsoYaw: torso.rotation.y,
                feet: ankles.map((ankle, i) => {
                    const knee = knees[i], hip = knee.parent;
                    const upper = knee.getAbsolutePosition().subtract(hip.getAbsolutePosition());
                    const lower = ankle.getAbsolutePosition().subtract(knee.getAbsolutePosition());
                    const bend = upper.subtract(lower).scaleInPlace(0.5);
                    const direction = ankle.getDirection(BABYLON.Axis.Z);
                    const shoe = mesh('player-shoe-' + sides[i]);
                    const {x, z} = shoe.getAbsolutePosition();
                    return {
                        x, z, direction: xyz(direction), bend: bend.length(),
                        lateral: bend.dot(hip.parent.getDirection(BABYLON.Axis.X)),
                        soleHeight: box(shoe).minimumWorld.y, floor: game.heightAt(x, z)
                    };
                })
            };
        },
        rest() {
            return {
                ...deltaPose(), bend: torso.rotation.x, roll: torso.rotation.z,
                feet: ankles.map((ankle, i) => ({
                    ankle: xyz(ankle.getAbsolutePosition()),
                    hip: xyz(knees[i].parent.getAbsolutePosition()),
                    up: ankle.getDirection(BABYLON.Axis.Z).y, knee: knees[i].rotation.x
                }))
            };
        },
        step() {
            return { z: player.position.z, heights: ankles.map(a => a.getAbsolutePosition().y),
                knees: knees.map(k => k.rotation.x) };
        },
        impact() {
            return { z: player.position.z, bend: torso.rotation.x, gap: boundsGap('remote-contact-a', 'z', false) };
        }
    };

    globalThis.ultimaCollisionProbes = {
        position,
        gaitClock(fixed) {
            // Use a stable simulation step for gait measurements across renderers.
            engine.getDeltaTime = fixed ? () => 1000 / 60 : getDeltaTime;
        },
        nodePosition: name => node(name) ? xyz(node(name).position) : null,
        meshExists: name => !!mesh(name),
        advance(seconds) {
            let elapsed = 0;
            return sampleUntil(30000, 'tempo limite para avançar a simulação', () => {
                elapsed += clock();
                if (elapsed >= seconds) return true;
            });
        },
        traverse({budget, axis, target}) {
            let elapsed = 0;
            return sampleUntil(15000, 'tempo limite no percurso de contato', () => {
                elapsed += clock();
                if (elapsed >= budget || player.position[axis] > target) return position();
            });
        },
        startTrace(kind) {
            if (observer) throw Error('já existe uma captura ativa');
            samples = [];
            observer = scene.onAfterRenderObservable.add(() => samples.push(capture[kind]()));
        },
        drainTrace() {
            const batch = samples;
            samples = [];
            return batch;
        },
        stopTrace() {
            scene.onAfterRenderObservable.remove(observer);
            observer = null;
            return this.drainTrace();
        },
        forceArms() {
            const {armA, armB, shoulderA, shoulderB} = armNodes();
            shoulderA.rotation.z = 0.8;
            shoulderB.rotation.z = -0.8;
            armA.computeWorldMatrix(true);
            armB.computeWorldMatrix(true);
            return armPose();
        },
        armPose,
        async armFrames(count) {
            const {shoulderA, shoulderB} = armNodes();
            const frames = [];
            for (let i = 0; i < count; i++) {
                shoulderA.rotation.z = 0.8;
                shoulderB.rotation.z = -0.8;
                await nextFrame();
                frames.push(armPose());
            }
            return frames;
        },
        resetShoulder() { node('player-shoulder-right').rotation.z = 0.055; },
        sideContactFrames(remote) {
            let elapsed = 0;
            const frames = [];
            // Capture position and clearance together during the approach.
            return sampleUntil(30000, 'lateral approach did not finish', () => {
                elapsed += clock();
                frames.push({ x: player.position.x, gap: boundsGap(remote, 'x', true) });
                if (elapsed >= 1.8) return frames;
            });
        },
        parents: names => names.map(name => node('player-' + name)?.parent?.name ?? null),
        torsoBend: () => torso.rotation.x,
        async logContact(positions) {
            game.resetPlayer();
            player.rotation.y = 0;
            const log = mesh('place-browser-log');
            const shoes = sides.map(side => mesh('player-shoe-' + side));
            const down = new BABYLON.Vector3(0, -1, 0);
            const result = [];
            for (const z of positions) {
                player.position.set(0, player.position.y, z);
                await new Promise(resolve => scene.onAfterRenderObservable.addOnce(() =>
                    scene.onAfterRenderObservable.addOnce(resolve)));
                const feet = shoes.map(shoe => {
                    const {centerWorld, minimumWorld} = box(shoe);
                    const ray = new BABYLON.Ray(new BABYLON.Vector3(centerWorld.x, 3, centerWorld.z), down);
                    const bark = ray.intersectsMesh(log).pickedPoint;
                    const floor = scene.pickWithRay(ray, m => m.name.startsWith('ground-')).pickedPoint;
                    return {sole: minimumWorld.y, floor: floor.y, log: bark?.y ?? null};
                });
                result.push({z, feet});
            }
            return result;
        },
        placeAtNativeTree() {
            const tree = scene.meshes.filter(m => m.name.startsWith('tree-tree.pine-trunk-') &&
                m.isEnabled() && Math.abs(m.position.x) > 6)
                .sort((a, b) => a.position.lengthSquared() - b.position.lengthSquared())[0];
            if (!tree) return null;
            player.position.set(tree.position.x, tree.position.y + 0.25, tree.position.z - 1.15);
            return xyz(tree.position);
        },
        conversation() {
            return {mouth: mesh('npc-mouth').scaling.y, listening: node('player-head-pivot').rotation.z,
                npcYaw: node('npc').rotation.y, playerYaw: player.rotation.y};
        },
        remotePose() {
            const remote = node('remote-gesture-test'), label = mesh('remote-label-gesture-test');
            return {actor: remote ? xyz(remote.position) : null, label: label ? xyz(label.position) : null,
                actorEnabled: remote?.isEnabled() ?? null, labelEnabled: label?.isEnabled() ?? null,
                arm: node('remote-gesture-test-shoulder-right')?.rotation.x ?? 0};
        },
        remoteShadow() {
            const sun = scene.getLightByName('sun');
            const generator = sun.getShadowGenerator(scene.activeCamera) ?? sun.getShadowGenerator();
            return generator.getShadowMap().renderList.some(m => m.name.includes('remote-gesture-test'));
        },
        camera() {
            const camera = scene.activeCamera;
            return {position: xyz(camera.position), forward: xyz(camera.getForwardRay().direction)};
        }
    };
    return true;
})()
