# Default humanoid, outfits and player postures

## Investigation

The fitted regions reuse the CC0 Innerscene body geometry and skin weights.
This follows [Babylon.js's skinned clothing guidance](https://doc.babylonjs.com/features/featuresDeepDive/mesh/bonesSkeletons).
Jakobsen's [Advanced Character Physics](https://www.cs.cmu.edu/afs/cs/academic/class/15462-s13/www/lec_slides/Jakobsen.pdf)
informed bounded substeps for the optional scarf. The scarf uses a single
damped hinge subject to gravity and quadratic relative air drag.

## Changes

The local player defaults to the Innerscene skin. The player's stored UUID
selects the shirt, trousers and boots palette and whether to attach a scarf.

Keyboard controls are Space for jump, C for crouch, X for crawl, Z for lying
face down and F for sitting on the ground. Repeating the posture key returns
to standing. Space first exits a lowered posture. Jumping uses an absolute
vertical trajectory, tests overhead clearance for authored boxes and checks
landing against the current support. Near tunnel volumes, jumping is disabled
until their vertical sweep is implemented.

## Validation and delivery

RulesSmoke and all 16 JavaScript tests passed. Chromium verified the default
skin, outfit persistence, scarf attachment and all five keyboard actions in
both renderer paths. The jump reached 0.62 m. The two-profile gait regression
also passed in both renderers.

[GitHub Pages build `0dd2bfa`](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46102218188)

This delivery covers the local player in both render paths. The standing
movement collider remains conservative for low postures. The low poses are
procedural and do not yet test knees, hands or the body against the ground.
Remote outfits and posture synchronization remain separate network work.
