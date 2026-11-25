# Shared articulated player in the garden and office

## Investigation

The office loaded the garden's signed character descriptor, then animated
four simple limb pivots. The garden already had a joint hierarchy and
contact-driven C# movement and animation.

## Changes

- Extracted the [character rig renderer](../../src/wwwroot/js/character-rig.js)
  for use in both environments. It creates the articulated body, mounts
  the signed character parts, and applies the same pose outputs.
- Interior walking uses the same [C# movement](../../engine/Ultima.Terrain/PlayerMovement.cs),
  pivots, foot contacts, limb solver and body pose as the garden. The office
  supplies a flat floor and its own solid-box collision resolver.
- The office submits one combined pose per frame for the character and camera.
  The existing indoor camera, viewport and mission controls were exercised
  by the office regression scenario.

## Validation and delivery

- Terrain WASM, Release publish and RulesSmoke passed. Chromium confirmed
  the office joints, knee flexion and ankle rotation during walking.
  Both garden renderer modes passed the office scenario locally.
- Camera switching and camera streaming passed locally. The garden's
  instrumented short-step regression verified foot-path shortening with
  zero body displacement locally and on the published build.
- The published office passed the joint-motion and traversal scenario.
  Verified revision `002eafade29693cb7a62be92ecd038155f204e37`.
  [Office](https://sufficit.github.io/sufficit-game-ultima/environments/?id=office)
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45724074868)
