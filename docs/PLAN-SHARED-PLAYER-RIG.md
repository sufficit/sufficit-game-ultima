# Shared player rig

1. [complete] Confirm that the office uses the signed garden model with a
   separate four-pivot walk cycle.
2. [complete] Share the articulated rig and C# player locomotion, with
   scene-specific collision and floor sampling.
3. [in progress] Verify animation parity, office traversal, and garden behavior,
   then publish and verify the hosted build.

Acceptance. Both environments apply the same articulated player rig with
hips, knees, ankles, elbows and wrists. The office uses the existing contact
solver and turn handling, and retains its interior collision and camera.


Release, terrain WASM and RulesSmoke passed. Chromium verified office
joint motion and traversal in both garden modes, camera switching and
the garden's short-step contact regression.

Next action. Publish and repeat the office and short-step checks on Pages.
