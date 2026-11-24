// A DLL fornece as matrizes assadas. O Babylon toca a paleta na GPU.
(function (global) {
    'use strict';

    function skinOf(group, x) {
        if (group === 'legs' || group === 'shoes') { return x < 0 ? 1 : 2; }
        if (group === 'arm-left' || group === 'hand-left') { return 3; }
        if (group === 'arm-right' || group === 'hand-right') { return 4; }
        return 0;
    }

    function skinMesh(scene, name, groups, material, skeleton, manager, capacity) {
        const positions = [], normals = [], colors = [], weights = [], jointIndices = [], indices = [];
        for (const { group, mesh, color } of groups) {
            const pos = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
            const norm = mesh.getVerticesData(BABYLON.VertexBuffer.NormalKind);
            const start = positions.length / 3;
            for (let vertex = 0; vertex < pos.length; vertex += 3) {
                positions.push(pos[vertex], pos[vertex + 1], pos[vertex + 2]);
                normals.push(norm[vertex], norm[vertex + 1], norm[vertex + 2]);
                colors.push(color.r, color.g, color.b, 1);
                weights.push(1, 0, 0, 0);
                jointIndices.push(skinOf(group, pos[vertex]), 0, 0, 0);
            }
            for (const index of mesh.getIndices()) { indices.push(start + index); }
        }
        const data = new BABYLON.VertexData();
        Object.assign(data, { positions, normals, colors, indices, matricesWeights: weights, matricesIndices: jointIndices });
        const mesh = new BABYLON.Mesh(name, scene);
        data.applyToMesh(mesh);
        mesh.material = material;
        mesh.skeleton = skeleton;
        mesh.numBoneInfluencers = 1;
        mesh.bakedVertexAnimationManager = manager;
        mesh.isPickable = false;
        mesh.alwaysSelectAsActiveMesh = true;  // cada instância já passou pelo frustum
        const matrices = new Float32Array(capacity * 16);
        const animation = new Float32Array(capacity * 4);
        mesh.thinInstanceSetBuffer('matrix', matrices, 16, false);
        mesh.thinInstanceSetBuffer('bakedVertexAnimationSettingsInstanced', animation, 4, false);
        mesh.setEnabled(false);
        return { mesh, matrices, animation, count: 0 };
    }

    function create(scene, count, entries, buildCoarse, terrain) {
        const engine = scene.getEngine();
        const textureKind = BABYLON.Constants.TEXTURETYPE_FLOAT;
        if (!engine.getCaps().textureFloat || !engine.getCaps().instancedArrays) {
            throw new Error('A GPU não oferece texturas de ossos e instâncias.');
        }

        const material = new BABYLON.StandardMaterial('crowd-material', scene);
        material.specularColor.set(0.025, 0.025, 0.025);
        material.emissiveColor.set(0.08, 0.08, 0.08);
        const groups = entries.map(({ desc, protos }, type) => {
            const clip = desc.crowdWalk;
            const skeleton = new BABYLON.Skeleton('crowd-skeleton-' + type, 'crowd-skeleton-' + type, scene);
            for (let i = 0; i < clip.bones; i++) {
                new BABYLON.Bone('bone-' + i, skeleton, null, BABYLON.Matrix.Identity());
            }
            const texture = BABYLON.RawTexture.CreateRGBATexture(
                new Float32Array(clip.matrices), (clip.bones + 1) * 4, clip.frames, scene,
                false, false, BABYLON.Texture.NEAREST_SAMPLINGMODE, textureKind);
            const manager = new BABYLON.BakedVertexAnimationManager(scene);
            manager.texture = texture;
            manager.setAnimationParameters(0, clip.frames - 1, 0, clip.fps);

            const high = protos.map((mesh, i) => ({ group: desc.groups[i].id, mesh, color: mesh.material.diffuseColor }));
            const low = high.filter(part => part.group !== 'eyes' && !part.group.startsWith('hand-'))
                .map(part => ({ ...part, mesh: buildCoarse(desc, part.group, part.mesh.material) }));
            const levels = [
                skinMesh(scene, 'crowd-high-' + type, high, material, skeleton, manager, count),
                skinMesh(scene, 'crowd-low-' + type, low, material, skeleton, manager, count)
            ];
            for (const part of low) { part.mesh.dispose(); }
            return { manager, levels, clip, height: desc.bounds.maxY };
        });

        const positions = terrain.CreateCrowd(count, Float64Array.from(groups, group => group.height));
        const matrices = Array.from({ length: count }, (_, i) => BABYLON.Matrix.RotationY(i * 2.39996)
            .setTranslationFromFloats(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]));

        const planes = BABYLON.Frustum.GetPlanes(BABYLON.Matrix.Identity());
        const planeData = new Float64Array(24);
        let nextCull = 0, cutoff = 26, distanceCulled = 0, frustumCulled = 0;
        function update(camera, seconds) {
            for (const group of groups) { group.manager.time += seconds; }
            const now = performance.now();
            if (now < nextCull) { return; }
            nextCull = now + 90;
            BABYLON.Frustum.GetPlanesToRef(camera.getTransformationMatrix(), planes);
            for (let i = 0; i < planes.length; i++) {
                const { normal, d } = planes[i], j = i * 4;
                planeData[j] = normal.x;
                planeData[j + 1] = normal.y;
                planeData[j + 2] = normal.z;
                planeData[j + 3] = d;
            }
            const { x, y, z } = camera.position;
            const result = terrain.CullCrowd(x, y, z, planeData, engine.getRenderHeight(), camera.fov, engine.getFps());
            const [visible, distant, hidden, threshold] = result;
            distanceCulled = distant;
            frustumCulled = hidden;
            cutoff = threshold;
            for (const { levels } of groups) for (const level of levels) { level.count = 0; }
            for (let i = 4; i < visible + 4; i++) {
                const id = result[i] >>> 1;
                const { levels, clip } = groups[id % groups.length];
                const level = levels[result[i] & 1];
                matrices[id].copyToArray(level.matrices, level.count * 16);
                const offset = level.count * 4;
                level.animation[offset + 1] = clip.frames - 1;
                level.animation[offset + 2] = id % clip.frames;
                level.animation[offset + 3] = clip.fps;
                level.count++;
            }
            for (const { levels } of groups) for (const level of levels) {
                level.mesh.setEnabled(level.count > 0);
                level.mesh.thinInstanceCount = level.count;
                if (level.count === 0) { continue; }
                level.mesh.thinInstanceBufferUpdated('matrix');
                level.mesh.thinInstanceBufferUpdated('bakedVertexAnimationSettingsInstanced');
            }
        }

        function status() {
            return {
                requested: count, distanceCulled, frustumCulled, fps: engine.getFps(), cutoff,
                types: groups.map(({ manager, levels }) => ({
                    ready: levels.filter(level => level.count).every(level => level.mesh.isReady(true)),
                    time: manager.time,
                    visible: levels.map(level => level.count),
                    vertices: levels.map(level => level.mesh.getTotalVertices())
                }))
            };
        }
        return { update, status };
    }

    global.UltimaCrowd = { create };
})(globalThis);
