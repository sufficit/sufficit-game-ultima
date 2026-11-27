# Reference humanoid

| File | Purpose |
| --- | --- |
| [humanoid-reference.blend](humanoid-reference.blend) | Editable Blender 4.5 LTS master with a skinned mesh and named armature. |
| [humanoid-reference.glb](humanoid-reference.glb) | Neutral glTF 2.0 reference for the future player skin binding. |
| [build_reference.py](build_reference.py) | Procedural authoring and deformation checks for this reference. |

This original Ultima reference uses the [repository MIT license](../../LICENSE).
It has four flat-color materials and no textures. The mesh forms one connected surface across the shoulders, pelvis and crotch.
Sleeves share a border with the torso and blend chest and shoulder weights. The wrists drive placeholder hands, while the
finger bones are present for later weighting. The reference has no gameplay
collider or animation clips. See the
[implementation plan](../../docs/PLAN-HUMANOID-SKINNED-RIG.md) for integration.

## Rest transforms

The 1.75 m reference shares the playable default arm pivot ratio of 0.80518
with the C# model. The sleeve opening follows separate measurements from
the sole to the acromion (1.435 m) and axilla (1.324 m).
[Shoulder reference study](../../docs/activities/202611270952-humanoid-shoulder-references.md)

Bone heads follow the [neutral pose contract](../../docs/USAGE-ANIMACOES.md#humanoid-bind-pose-contract)
and stable C# joint IDs. The implicit actor root becomes a bone at the origin.
During authoring, Ultima coordinates `(x, y, z)` become Blender
`(x, -z, y)`. The GLB export converts back to Y-up, so its bind pivots
match the C# coordinates in metres.

A bone tail points toward its first child with a distinct head when available.
Leaf bones and coincident pivots use a short positive Blender Z tail where
needed. A tail defines Blender's local orientation. Retargeting must use the
exported rest rotations and the skin's inverse bind matrices instead of
treating the bone axes as identity.

## Rebuild and validate

From the repository root, use .NET 10 and Blender 4.5 LTS. To rebuild the
procedural reference, run the following commands. Artistic edits to the
master can also be exported from Blender with skinning enabled and animation
export disabled.

```bash
dotnet build tools/Ultima.RigExport -c Release
dotnet run --project tools/Ultima.RigExport -c Release --no-build > /tmp/ultima-anatomy.json
blender --background --python assets/characters/build_reference.py -- \
  /tmp/ultima-anatomy.json assets/characters/humanoid-reference
```

The checked export uses Blender 4.5.3 LTS. It contains 74 bones, one skin,
and four material primitives. The Blender source mesh has 679 vertices.
The build checks closed mesh edges, normalized weights, and independent
shoulder, hip, knee and ankle deformation. The production Babylon.js version imported
the GLB in Chromium and verified its skin, exact hierarchy, joint pivots
within 0.05 mm, and deformation at both shoulders, the hip, knee and ankle. The Khronos glTF
validator reported zero errors and warnings.
