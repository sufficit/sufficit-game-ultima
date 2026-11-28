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
3. [in progress] Drive the skin from the C# pose. Map model rest space to the
   imported bone space, keep the current foot contacts and IK, and verify
   equivalent poses in the worker and main-thread renderer. Define when to use
   authored clips and when to apply procedural contact corrections.
   First, prepare and verify an optimized Innerscene human base mesh as the
   maintainer's visual reference. Then exercise the C# pose on that mesh in
   the game before considering it for the default avatar.
   Fit its legs to the final procedural ankle targets with a two-bone solve
   that preserves segment lengths, knee bend direction, and sole rotation.
   Add a coordinated pelvis, chest and arm gait sampled from the existing
   stride phase. Verify whole-body motion and retain the ankle alignment
   checks before publishing the preview.
   Measure vertical bounce on a repeatable route. Tune travel pace and damp
   presentation height against real support while keeping both ankle targets.
   Repeat the measurements and verify the published preview.
   Calibrate the imported arm rest pose against the motion comparison, add
   damped hand follow-through and relaxed finger curl, then re-record the
   profile and verify the published build.
   Reduce the imported finger splay and drive each phalanx with its own
   damped response to the preceding segment. Compare the hand in profile
   and verify the browser regressions before publishing.
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
the torso. The isolated Chromium test verified both shoulders after that change.

The playable avatar and Blender reference now share the MPFB2-based default
shoulder pivots. The playable sleeve geometry follows separate external
heights and width from one central ANSUR II profile. The Blender model uses
the same exterior heights at the sleeve opening.
[Measured references](activities/202611270952-humanoid-shoulder-references.md)

## Checkpoint 3 progress

The [Innerscene preview](../assets/characters/README.md#innerscene-player-preview)
uses 18,000 triangles and 53 bones. The local player can select it with
`?avatar=innerscene`. Main-thread and worker Chromium runs verified that the
body renders and the left knee rotates when the player walks. The preview
carries pose deltas onto the imported rig while C# still drives contacts.
A corrupted download was rejected by SHA-256 and left the procedural player
visible. The default URL made no preview asset request. RulesSmoke, all 16
JavaScript tests, and the Blazor Release publish passed.

The Innerscene preview now passes both leg chains to one C# two-bone solve
after gameplay contacts settle. The solve preserves bone lengths and the
incoming knee plane, lowers the visual pelvis within a 15 cm allowance, and
clamps unreachable targets. Babylon applies the solved joint positions while
keeping the posed sole orientation.

The previous release averaged 11 cm of horizontal ankle error during walking.
The `skin-feet` Chromium scenario passed for the default 1.75 m body and a
1.90 m body with different leg proportions, in both renderer modes. It
checked walking, stopping, a 180-degree reversal and preservation of both
segment lengths. Horizontal error stayed under 1 mm in the sampled frames.
The same scenario passed on Pages build `653ab919cae19390ffaca1c07d079024eb2c8584`
in both modes. [Pages workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46076513787)

The whole-body preview adds coordinated hip and thorax rotations, lateral
weight transfer, clavicle movement and phased elbow and wrist motion. The
C# curves use the existing foot-cycle phase, scale with gait weight and
allow more elbow flexion as pace increases. These animation amplitudes are
artistic tuning values. The biomechanics references describe coordination
and timing across walking speeds.
[Collins, Adamczyk and Kuo](https://pmc.ncbi.nlm.nih.gov/articles/PMC2817299/)
and [Bruijn et al.](https://pubmed.ncbi.nlm.nih.gov/17669652/)

RulesSmoke verified phase relationships and fade to rest. Chromium sampled
both hands alternating fore and aft in actor space, with 36–40 cm of travel
per hand during walking. The same test verified rotation in the pelvis,
thorax, both clavicles and both elbows and retained sub-millimetre ankle
alignment across both profiles and renderers.
The published build `8ac8c38c7ff4df99ee632c7cb31da4f5119cd500` reproduced
those checks with 34–39 cm of hand travel across the four runs. The preceding
build already moved the hands through about 27 cm in the default profile,
but published joint samples showed effectively constant rotations at the
pelvis, thorax, clavicles and elbows.
[Pages workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46081474377)

## Vertical stability and pace

The previous preview inherited the cyclic height of the gameplay leg solve.
It showed 5.7 cm of head-height excursion in a sampled Garden route. The
preview now filters its body height against the support surface, leaves leg
reach in reserve through the stride, and limits the final height to the two
ankle targets. Walking is capped at 1.65 m/s, with a pace-dependent stride,
gentler acceleration and braking, and a shallower swing arc whose vertical
velocity starts and ends at zero.

The local repeat of the route showed about 2 cm of head-height excursion.
On the level subsegment, `skin-feet` showed 0.4–0.5 cm for the default body
and 1.6–1.8 cm for the custom body in the two Chromium renderer modes. All
four runs retained ankle alignment, leg length and arm swing.
The published build `6c47cc641a9c303bc94a9d351d9d2bf5842b3523` also passed
all four runs. Head height varied by at most 3.2 mm for the default body and
16 mm for the custom body in the sampled level subsegment.
[Pages workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46083190557)

The installed [unity-animation](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/tree/c6ca6c41c839b68319ffc08430b99ab768debc32/skills/unity/unity-animation)
and [physics-tuning](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/tree/c6ca6c41c839b68319ffc08430b99ab768debc32/skills/disciplines/physics-tuning)
skills informed speed-driven blending, damping and the choice of one owner
for the final visual height.

## Arm and hand follow-through

A side-view recording of the previous preview measured 46 degrees of elbow
flexion at rest and 50–63 degrees during walking, compared with 2–27 degrees
in UFSC's selected neutral male motion-capture trial. The recorded index,
middle and thumb joints kept one local rotation throughout the old clip.

The preview now corrects the imported forearm rest orientation. Two damped
states per wrist respond to the forearm swing. The fingers use a progressive
resting curl and a small wrist-dependent change. The new local recording
measured about 13 degrees at rest and 17–30 degrees during walking. Both
renderer paths and both body profiles passed wrist, finger and ankle checks.
[Published build `a73b02f`](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46099867470)
passed all four browser runs and supplied the after footage for the comparison.
[UFSC locomotion dataset](https://tecmidia.ufsc.br/en/locomotion-dataset/)

The next pass brought the finger bases toward the middle finger's direction
in the palm plane and increased their resting curl. The torso, shoulder and
elbow pitch feed the damped wrist, and each finger segment follows the previous
one with decreasing amplitude. A unit test verifies the ordered peaks and
settling. Side-view sampling reduced the index-to-pinky distal span from
1.30 to 1.09 times the base span. Both body profiles and both renderer paths
passed the local regression scenario.
All four runs also passed on [published build `52ac4e4`](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46100686392),
which supplied the after footage for the finger comparison.

## Arm carriage and ankle follow-through

An additional mirrored 0.075 radians of shoulder adduction moved each wrist
3.2 cm inward in the default standing profile. During swing, the preview
damps ankle pitch against a toe-down and heel-up target curve, limits rotation
by foot clearance, and resets the swing contribution during support. The
fixed-step check verifies delayed pitch, reversal and a stable planted foot.
Both profiles and renderer paths passed the browser checks for resting wrist
spacing, arm and finger motion, and ankle alignment.

Next action. Exercise the same retargeting on a controlled slope and low
obstacle. Measure stance drift against the world and frame cost in both
renderers before considering the preview for the default avatar.

[Kovar, Schreiner and Gleicher's footskate study](https://graphics.cs.wisc.edu/Papers/2002/KSG02/)
motivates treating support positions as constraints during retargeting.
