# Default humanoid, outfits and player postures

Make the verified Innerscene humanoid the local player default, give each player
identity a repeatable outfit, and add keyboard locomotion and posture actions.

1. [complete locally] Verify asset sources and implement the default skin, seeded
   clothing, and one lightweight accessory with gravity and air drag.
2. [complete locally] Implement jumping, crouching, crawling, lying and sitting with C#
   state and shared renderer integration.
3. [in progress] Verify identity stability, visual coverage, the actions, both
   renderer paths and the live release. Record any remaining limitations.

Next action. Repeat the outfit and posture checks on the published build.

The fitted regions reuse the CC0 Innerscene body geometry and skin weights.
This follows [Babylon.js's skinned clothing guidance](https://doc.babylonjs.com/features/featuresDeepDive/mesh/bonesSkeletons).
Jakobsen's [Advanced Character Physics](https://www.cs.cmu.edu/afs/cs/academic/class/15462-s13/www/lec_slides/Jakobsen.pdf)
informed bounded substeps for the optional scarf. The scarf uses a single
damped hinge subject to gravity and quadratic relative air drag.

Keyboard controls are Space for jump, C for crouch, X for crawl, Z for lying
face down and F for sitting on the ground. Repeating the posture key returns
to standing. Space first exits a lowered posture. Jumping uses an absolute
vertical trajectory, tests overhead clearance for authored boxes and checks
landing against the current support. Near tunnel volumes, jumping is disabled
until their vertical sweep is implemented.

This delivery covers the local player in both render paths. The standing
movement collider remains conservative for low postures. The low poses are
procedural and do not yet test knees, hands or the body against the ground.
Remote outfits and posture synchronization remain separate network work.
