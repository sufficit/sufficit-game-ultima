# Keyboard look controls

1. [complete] Route WASD to movement and arrow keys to camera look in C#.
   Update the visible controls in both environments.
2. [complete] Verify that arrow keys turn and tilt every view without moving
   the player. Run input regression tests.
3. [in progress] Publish and verify the live build, then record the activity.

Acceptance. Arrow keys act like camera drag in the garden and office, including
front third person. WASD moves the player. Browser text fields keep their
normal behavior.

Validated locally with the Release publish, RulesSmoke, and the camera-views
and input-devices scenarios in Chromium with SwiftShader, using the worker
and main-thread fallback.

Next action. Push the change, verify the Pages build and repeat camera-views
on the published site.
