(function (scope) {
    'use strict';

    scope.createWorldPlacements = function ({
        scene, lookupAsset, getPlayer, getShadows, terrainHeight, trackGlbObstacle, trackGatherable
    }) {
        const placements = new Map();
        const pendingPlacements = new Map();

        const gardenLanterns = new Map();
        const gardenLights = [];
        const lanternPrototypes = new Map();
        const tintedPrototypes = new Map();
        let lanternMetal, lanternGlass;

        function placementParameter(p, name, fallback, min, max) {
            const value = p.parameters?.[name];
            return Number.isFinite(value) ? Math.max(min, Math.min(max, value)) : fallback;
        }

        function rgbColor(rgb) {
            return BABYLON.Color3.FromInts((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }

        function tintedPrototype(asset, rgb) {
            if (rgb === 0xffffff) return null;
            const key = asset.name + ':' + rgb;
            let variant = tintedPrototypes.get(key);
            if (variant) return variant;
            const materials = [];
            const tint = material => {
                if (!material) return material;
                const copy = material.clone(key + ':' + material.name);
                materials.push(copy);
                if (material.subMaterials) copy.subMaterials = material.subMaterials.map(tint);
                else if (copy.albedoColor) copy.albedoColor = rgbColor(rgb);
                else if (copy.diffuseColor) copy.diffuseColor = rgbColor(rgb);
                return copy;
            };
            const proto = asset.clone(key, null, true);
            proto.material = tint(asset.material);
            proto.isVisible = false;
            proto.isPickable = false;
            variant = { proto, instances: 0, materials, key, cache: tintedPrototypes };
            tintedPrototypes.set(key, variant);
            return variant;
        }

        function gardenLantern(p, poleHeight, lightIntensity, lumens, rgb) {
            if (!lanternMetal) {
                lanternMetal = new BABYLON.StandardMaterial('garden-lantern-metal', scene);
                lanternMetal.diffuseColor = new BABYLON.Color3(0.16, 0.13, 0.09);
                lanternGlass = new BABYLON.StandardMaterial('garden-lantern-glass', scene);
                lanternGlass.diffuseColor = new BABYLON.Color3(1, 0.77, 0.36);
                lanternGlass.emissiveColor = new BABYLON.Color3(1, 0.58, 0.17);
            }
            const key = poleHeight + ':' + rgb;
            let variant = lanternPrototypes.get(key);
            if (!variant) {
                const base = BABYLON.MeshBuilder.CreateCylinder('lantern-base', { height: 0.14, diameter: 0.34, tessellation: 8 }, scene);
                const post = BABYLON.MeshBuilder.CreateCylinder('lantern-post', { height: poleHeight, diameter: 0.10, tessellation: 8 }, scene);
                const globe = BABYLON.MeshBuilder.CreateBox('lantern-glass', { width: 0.26, height: 0.32, depth: 0.26 }, scene);
                const roof = BABYLON.MeshBuilder.CreateCylinder('lantern-roof', { height: 0.13, diameterTop: 0.10, diameterBottom: 0.42, tessellation: 4 }, scene);
                base.position.y = 0.07;
                post.position.y = 0.07 + poleHeight / 2;
                globe.position.y = poleHeight + 0.17;
                roof.position.y = poleHeight + 0.39;
                base.material = post.material = roof.material = lanternMetal;
                const glass = rgb === 0xffab4d ? lanternGlass : lanternGlass.clone('garden-glass-' + rgb);
                if (glass !== lanternGlass) glass.diffuseColor = glass.emissiveColor = rgbColor(rgb);
                globe.material = glass;
                const proto = BABYLON.Mesh.MergeMeshes([base, post, globe, roof], true, true, undefined, false, true);
                proto.name = 'garden-lantern-prototype-' + key;
                proto.isVisible = false;
                proto.isPickable = false;
                variant = { proto, instances: 0, materials: glass === lanternGlass ? [] : [glass], key, cache: lanternPrototypes };
                lanternPrototypes.set(key, variant);
            }
            const mesh = variant.proto.createInstance('place-' + p.uid);
            mesh.isVisible = true;
            mesh.isPickable = true;
            const settings = { poleHeight, lightIntensity, lumens, rgb };
            gardenLanterns.set(mesh, settings);
            mesh.onDisposeObservable.addOnce(() => gardenLanterns.delete(mesh));
            return { mesh, variant };
        }

        function updateGardenLights() {
            if (!gardenLanterns.size && !gardenLights.length) return;
            const player = getPlayer();
            if (!player) return;
            const nearest = Array.from(gardenLanterns, ([mesh, settings]) => {
                const dx = mesh.position.x - player.position.x, dz = mesh.position.z - player.position.z;
                return { mesh, settings, distance: dx * dx + dz * dz };
            }).filter(item => item.distance < 400 && (item.settings.lumens ?? item.settings.lightIntensity) > 0).sort((a, b) => a.distance - b.distance).slice(0, 2);
            for (let i = 0; i < 2; i++) {
                let light = gardenLights[i];
                if (!nearest[i]) { light?.setEnabled(false); continue; }
                if (!light) {
                    light = new BABYLON.PointLight('garden-light-' + i, BABYLON.Vector3.Zero(), scene);
                    light.range = 7;
                    gardenLights[i] = light;
                }
                const { mesh, settings } = nearest[i];
                light.position.copyFrom(mesh.position);
                light.position.y += (settings.poleHeight + 0.17) * mesh.scaling.y;
                const mode = settings.lumens === null
                    ? BABYLON.Light.INTENSITYMODE_AUTOMATIC
                    : BABYLON.Light.INTENSITYMODE_LUMINOUSPOWER;
                if (light.intensityMode !== mode) light.intensityMode = mode;
                light.intensity = settings.lumens ?? settings.lightIntensity;
                if (light._ultimaRgb !== settings.rgb) {
                    light.diffuse = rgbColor(settings.rgb);
                    light._ultimaRgb = settings.rgb;
                }
                light.setEnabled(true);
            }
        }

        function spawnPlacement(p) {
            if (!p || !p.uid) { return; }
            const lamp = p.asset === 'prop.lantern';
            const asset = lookupAsset(p.asset);
            if (!lamp && !asset) {
                pendingPlacements.set(p.uid, p);
                return;
            }

            const scale = p.scale || 1;
            const sx = placementParameter(p, 'scaleX', scale, 0.05, 8);
            const height = placementParameter(p, 'heightM', null, 0.1, 56);
            let sy = placementParameter(p, 'scaleY', scale, 0.05, 8);
            if (height !== null && asset) {
                const bounds = asset.proto.getBoundingInfo().boundingBox;
                const baseHeight = bounds.maximum.y - bounds.minimum.y;
                if (baseHeight > 0) sy = height / baseHeight;
            }
            const tint = placementParameter(p, 'tintRgb', 0xffffff, 0, 0xffffff);
            const sz = placementParameter(p, 'scaleZ', scale, 0.05, 8);
            const yaw = placementParameter(p, 'rotationY', p.rot || 0, -2 * Math.PI, 2 * Math.PI);
            const poleHeight = lamp ? placementParameter(p, 'poleHeight', 1.4, 0.5, 5) : null;
            const intensity = lamp ? placementParameter(p, 'lightIntensity', 1.05, 0, 3) : null;
            const lumens = lamp ? placementParameter(p, 'lightLumens', null, 0, 5000) : null;
            const rgb = lamp ? placementParameter(p, 'lightRgb', 0xffab4d, 0, 0xffffff) : null;
            const existing = placements.get(p.uid);
            const sameAnchor = existing && existing.data.x === p.x && existing.data.y === p.y && existing.data.z === p.z;
            const y = sameAnchor ? existing.mesh.position.y : (p.y || terrainHeight(p.x, p.z)) - 0.02;
            if (existing) {
                const mesh = existing.mesh;
                const previous = gardenLanterns.get(mesh);
                if (existing.data.asset === p.asset && existing.tint === tint &&
                    (!lamp || (previous?.poleHeight === poleHeight && previous.rgb === rgb)) &&
                    sameAnchor && mesh.rotation.y === yaw && mesh.scaling.x === sx && mesh.scaling.y === sy && mesh.scaling.z === sz) {
                    if (previous) Object.assign(previous, { lightIntensity: intensity, lumens });
                    existing.data = p;
                    return;
                }
                removePlacement(p.uid);
            }
            const lantern = lamp ? gardenLantern(p, poleHeight, intensity, lumens, rgb) : null;
            const variant = lantern?.variant ?? tintedPrototype(asset.proto, tint);
            const proto = variant?.proto ?? asset.proto;
            const inst = lantern?.mesh ?? proto.createInstance('place-' + p.uid);
            if (variant) variant.instances++;
            inst.position.set(p.x, y, p.z);
            inst.rotation.y = yaw;
            inst.scaling.set(sx, sy, sz);
            try { getShadows()?.addShadowCaster(inst); } catch { /* The shadow generator may have been disposed. */ }
            trackGlbObstacle(inst, proto, p.asset, true);
            trackGatherable(inst, 'placed:' + p.uid, p.asset, false);
            placements.set(p.uid, { mesh: inst, data: p, tint, variant });
        }

        function flushPendingPlacements() {
            const pending = Array.from(pendingPlacements.values());
            pendingPlacements.clear();
            for (const p of pending) { spawnPlacement(p); }
        }

        function removePlacement(uid) {
            const placement = placements.get(uid);
            if (placement) {
                const { mesh, variant } = placement;
                try { getShadows()?.removeShadowCaster(mesh); } catch { /* The shadow generator may have been disposed. */ }
                mesh.dispose();
                placements.delete(uid);
                if (variant && --variant.instances === 0) {
                    variant.cache.delete(variant.key);
                    variant.proto.dispose();
                    for (const material of variant.materials) material.dispose();
                }
            }
            pendingPlacements.delete(uid);
        }

        function setPlacements(list) {
            const seen = new Set();
            for (const p of list || []) { if (p && p.uid) { seen.add(p.uid); spawnPlacement(p); } }
            for (const uid of placements.keys()) { if (!seen.has(uid)) { removePlacement(uid); } }
            for (const uid of pendingPlacements.keys()) { if (!seen.has(uid)) { pendingPlacements.delete(uid); } }
        }

        function applyPlacementOp(op) {
            if (op.kind === 'place') { spawnPlacement(op); return true; }
            if (op.kind === 'remove') { removePlacement(op.uid); return true; }
            if (op.kind !== 'customize') return false;
            const placement = placements.get(op.uid)?.data ?? pendingPlacements.get(op.uid);
            if (!placement) return false;
            spawnPlacement({ ...placement, parameters: op.parameters });
            return true;
        }

        return { applyPlacementOp, setPlacements, updateGardenLights, flushPendingPlacements };
    };
})(globalThis);
