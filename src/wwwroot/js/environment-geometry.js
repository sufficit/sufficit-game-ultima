(function () {
    'use strict';

    function build(B, scene, settings, baseUrl) {
        for (const source of settings.lights ?? []) {
            const light = new B.PointLight('interior-light', new B.Vector3(source.x, source.y, source.z), scene);
            light.diffuse = new B.Color3(1, .94, .84);
            light.range = 8;
            light.intensity = source.intensity;
        }

        const mats = new Map();
        const textures = new Map();
        function material(color, finish) {
            const key = color + '/' + (finish ?? '');
            if (!mats.has(key)) {
                const mat = new B.StandardMaterial(key, scene);
                mat.diffuseColor = B.Color3.FromHexString(color);
                mat.specularColor.set(.13, .14, .16);
                mat.maxSimultaneousLights = 5;
                if (finish === 'glass') {
                    mat.alpha = .25;
                    mat.specularPower = 90;
                    mat.backFaceCulling = false;
                }
                if (finish === 'light' || finish === 'screen') {
                    mat.emissiveColor = mat.diffuseColor.scale(finish === 'light' ? .8 : .45);
                }
                if (['wood', 'carpet', 'stone'].includes(finish)) {
                    if (!textures.has(finish)) {
                        const textureUrl = new URL('materials/interiors/' + finish + '.webp', baseUrl);
                        const texture = new B.Texture(textureUrl.href, scene);
                        texture.anisotropicFilteringLevel = 8;
                        textures.set(finish, texture);
                    }
                    mat.diffuseTexture = textures.get(finish);
                    mat.specularColor.scaleInPlace(finish === 'carpet' ? .07 : .65);
                }
                mat.freeze();
                mats.set(key, mat);
            }
            return mats.get(key);
        }

        const solids = [];
        const opaque = new Map();
        const stationPieces = [];
        for (const box of settings.boxes) {
            const mesh = B.MeshBuilder.CreateBox('environment-piece', {
                width: box.width, height: box.height, depth: box.depth,
            }, scene);
            mesh.position.set(box.x, box.y, box.z);
            mesh.material = material(box.color, box.material);
            mesh.isPickable = false;
            mesh.freezeWorldMatrix();
            mesh.receiveShadows = true;
            if (box.solid) solids.push(mesh);
            if (!settings.walk && box.station) stationPieces.push({ mesh, box });
            else if (!box.solid && box.material !== 'glass') {
                if (!opaque.has(mesh.material)) opaque.set(mesh.material, []);
                opaque.get(mesh.material).push(mesh);
            }
        }
        for (const group of opaque.values()) {
            if (group.length < 2) continue;
            const combined = B.Mesh.MergeMeshes(group, true, true);
            if (combined) {
                combined.isPickable = false;
                combined.freezeWorldMatrix();
            }
        }

        for (const sign of settings.signs ?? []) {
            const plate = B.MeshBuilder.CreatePlane('environment-sign', { width: sign.width, height: sign.height }, scene);
            plate.position.set(sign.x, sign.y, sign.z);
            plate.rotation.y = Math.PI;
            const graphic = new B.DynamicTexture('environment-sign-text', { width: 768, height: 192 }, scene);
            const context = graphic.getContext();
            context.fillStyle = '#28332f';
            context.fillRect(0, 0, 768, 192);
            context.fillStyle = '#f6ead7';
            context.font = '600 72px sans-serif';
            context.textAlign = 'center';
            context.textBaseline = 'middle';
            context.fillText(sign.text, 384, 98, 700);
            graphic.update();
            const label = new B.StandardMaterial('environment-sign-material', scene);
            label.diffuseTexture = label.emissiveTexture = graphic;
            label.emissiveColor.set(.58, .58, .58);
            plate.material = label;
            plate.isPickable = false;
        }

        return {
            solids,
            selectStation(id) {
                for (const { mesh, box } of stationPieces)
                    mesh.material = material(box.station === id ? '#397B78' : box.color, box.material);
            }
        };
    }

    globalThis.UltimaEnvironmentGeometry = { build };
})();
