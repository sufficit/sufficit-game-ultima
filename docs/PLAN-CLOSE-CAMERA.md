# Close third-person camera

Replace the front camera with close third person in the garden and office.
Keep the existing elevated and first-person views.

1. [complete] Implement close rear framing, character aim and mouse look.
2. [complete] Validate switching, look, movement and collision in both environments.
3. [in progress] Verify the published change and archive this plan.

Acceptance. The third view shows the character from behind at close range.
Mouse movement aims the character without holding a button. Escape releases
mouse capture. Keyboard, touch and gamepad retain their existing bindings.

Validated locally with the terrain WebAssembly build, Release publish,
RulesSmoke, and Chromium with SwiftShader. The camera-views scenario
passed in worker and main-thread modes. The camera-streaming, office and
input-devices regressions also passed.

Next action. Push main and verify camera-views on the published revision.
