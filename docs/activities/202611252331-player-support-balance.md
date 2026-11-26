# Foot support during aimed locomotion

## Investigation

Close third person can hold the avatar's aim while moving sideways or
backward. The gait placed new steps along the aim direction even when
the avatar traveled elsewhere. On the previous published build, the
browser regression measured both ankles on the same side of the body
during a sidestep, about 62 cm and 45 cm from the body center.

## Changes

- The [C# foot solver](../../engine/Ultima.Terrain/PlayerFootContacts.cs)
  uses actual travel to place the next step. A lateral or backward
  movement shortens the stride. In a sidestep, the outward foot leads
  and each foot lands in its own lane.
- When aimed translation stops, the feet return under the hips one at a time.
  Turning in place uses compact arc steps and keeps one foot grounded.
- The garden and office use these same rules. Existing terrain and
  obstacle queries supply foot contact height.

## Validation and delivery

- The terrain WebAssembly build, Release publish and RulesSmoke passed.
  RulesSmoke exercised both sidestep directions, backward travel,
  stopping and a stationary turn.
- Chromium measured the actual rendered ankle positions while moving
  laterally and after stopping in both environments and renderer modes.
  Obstacle stepping, camera streaming and office traversal passed locally.
- The same rendered stance check passed on published revision
  `992e1626e4545ffc3d2db89d9ebaa7fbf645ffbd` in both renderer modes.
  [GitHub Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45742601121)
