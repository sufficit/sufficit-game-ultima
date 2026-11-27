# Humanoid rig and skinned reference model

The existing humanoid anatomy is the runtime source for joint names, neutral
coordinates and proportions. Use it to rig one reference humanoid, keep motion
and contact solving in C#, and validate the result on the existing game paths.

1. [complete] Specify the neutral pose, axes and angular limits. Keep stable
   joint IDs and hierarchy. Export primary flexion metadata and verify the
   contract and custom body proportions in RulesSmoke.
2. [complete] Author a reference humanoid mesh and armature in Blender. Map it to
   the game IDs, document rest-space transforms, weight the mesh, and export a
   GLB. Check the imported skin, bind pose and deformation at hips, knees and
   ankles before extending to arms and hands.
3. [pending] Drive the skin from the C# pose. Map model rest space to the
   imported bone space, keep the current foot contacts and IK, and verify
   equivalent poses in the worker and main-thread renderer. Define when to use
   authored clips and when to apply procedural contact corrections.
4. [pending] Validate walking, stopping, a 180° turn, stepping over a low log,
   and walking on a slope with two different body profiles. Measure planted
   foot drift and frame cost against the current rig under the same conditions.
   Check joint motion, mesh deformation, and the live build before delivery.

## Checkpoint 1 evidence

The C# anatomy export preserves all 73 neutral pivots, parent links and motion
types from the joint map. It supplies primary axes and authoring limits to 69
pivots. RulesSmoke verifies the contract and custom proportions. The JavaScript
suite, WASM build and Blazor Release publish passed. The local Chromium test
verified a custom profile and the imported metadata in the Garden and Office
with both renderer modes.
The same Chromium scenario passed on the public Pages build
`a68bcad84ca6d5bbce3524529f4064d9f047de5c` in both renderer modes.
[Pages workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45995042423)

## Checkpoint 2 evidence

The [Blender master and GLB reference](../assets/characters/README.md) derive
their 74 bones from the C# neutral pose, including the actor root. The closed
lower-body surface blends weights across the hip, knee and ankle. Khronos
validation reported zero errors and warnings. Babylon.js in Chromium verified
the skin, the named hierarchy, rest pivots within 0.05 mm and independent
deformation of those joints. The reference was refined to join the sleeves to
the torso. It uses a 0.82 shoulder-to-standing-height ratio for its 1.75 m
profile. The isolated Chromium test verified both shoulders after the change.

Next action. Bind the existing C# pose to the GLB rest transforms in the main
and worker renderer paths, using the current joint map and foot contacts.
