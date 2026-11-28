# Relaxed resting pose and elbow swing

## Investigation

The old lying pose used clearance proxies and the camera floor, which
includes a 3 cm standing offset. It centered the hips while leaving the
hands and head visibly suspended. The earlier walk retarget moved the
elbows through only about 9° relative to each upper arm.

## Changes

- Calibrated the supine pose against the dressed Innerscene geometry.
  The pelvis anchors the mesh to the unpadded floor plane. The neck,
  extremities, and palm roll are fitted separately. A slight head turn
  and asymmetric limbs reduce the rigid silhouette.
- Added a damped elbow response in C#. The forearm extends behind a
  forward swing and flexes on reversal. The wrist and fingers receive
  the resulting forearm motion in sequence. The full walk cycle retains
  the existing authored timing and foot contact solve.

## Validation and delivery

- Built the terrain WebAssembly and published the Release Blazor app.
  RulesSmoke and all 16 JavaScript tests passed.
- Chromium `skin-feet` and `outfit-posture` passed in both modes. The
  elbow test samples a full stride on the default and 1.90 m profiles.
  The posture test checks the deformed mesh at the head, back, hips,
  hands, and heels. All seven regions were within about 1 cm of the
  floor in the level test area.
- The same side-view walk capture gave approximately 9° of elbow excursion
  before the change and 16° afterward on the default avatar.
- Verified the manifest, resting pose asset, and Chromium scenarios on
  the [Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46145645523)
  for commit `09d8855`.
  The 1.90 m profile's head-height range exceeded the 2.5 cm threshold
  on the initial production runs (2.6–2.9 cm) and passed on isolated
  reruns in both modes. The elbow excursion passed on those profiles
  and the resting contacts matched in both modes.
