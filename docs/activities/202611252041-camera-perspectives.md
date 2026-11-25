# Camera perspectives in the garden and interiors

## Investigation

The garden used an orbital follow camera and the office used an interior
follow camera. Both already had collision handling specific to their
geometry. A shared perspective cycle could preserve each controller.

## Changes

- Added rear third person, first person, and front third person to both
  environments. The front camera uses the avatar's heading to show its face.
- First person uses eye height and hides the local avatar from view. The
  garden keeps its orbital settings when returning to third person. Both
  external views apply the existing environment collision rules.
- The C# cycle is driven by V, the middle mouse button, R3, or
  **Alternar câmera**. The controls send one change per press.

## Validation and delivery

- Release publish, terrain WASM build, and RulesSmoke passed. Chromium
  verified both environments, camera placement, avatar visibility, all
  four controls, and holding a simulated R3 button.
- The garden camera streaming and office movement regressions passed
  locally. The perspective scenario passed on GitHub Pages in worker and
  main modes for revision `46c1fa9f9ff677f72c6c1f30f7c17ef4e5453c88`.
  [Garden](https://sufficit.github.io/sufficit-game-ultima/)
  [Office](https://sufficit.github.io/sufficit-game-ultima/environments/?id=office)
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45722901857)
