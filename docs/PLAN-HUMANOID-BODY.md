# Humanoid body profiles

1. [complete] Trace the model, rig, camera, physics and environment data contracts.
2. [complete] Implement humanoid species and resolve per-character body properties in C#.
3. [complete] Validate the default and a custom body in both environments and renderer modes.
4. [in progress] Publish, verify and archive this plan.

Acceptance. Homo sapiens is a humanoid species. A validated character profile
supplies the model, joint positions, camera and collision measurements.
Existing environments keep the current player appearance by default.

Release, RulesSmoke and Chromium passed. Browser tests loaded a 2.02 m
profile in the garden and office in both renderer modes and checked the
signed model, joint positions, eye height and the office wall clearance.
Default camera, obstacle step and office traversal regressions passed.

Next action. Push main and verify the custom profile on GitHub Pages.
