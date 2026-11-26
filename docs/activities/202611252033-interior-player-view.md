# Full-viewport office and third-person player view

## Investigation

Walkable environments used a padded content container and a first-person
camera. The existing terrain view provided the reference for a full-viewport
canvas, overlay controls, and a visible player.

## Changes

- Walkable scenes occupy the full viewport. The scene name, mission button,
  and environment selector sit over the canvas with safe-area offsets.
- The office uses the garden's `character.villager` descriptor through the
  existing SHA-256-verified model loader. The interior renderer applies a
  distance-driven walk cycle and displays the player in third person.
- The C# camera controller at the time
  retracts the camera before solid geometry and compensates for tight
  spaces. Motion and collisions remain in C#.

## Validation and delivery

- Release publish and RulesSmoke passed. Chromium verified canvas bounds on
  desktop and mobile, the signed garden model, camera clearance at the rear
  wall, the meeting-room door, the glass partition, and mission controls.
- The browser scenario passed locally and on GitHub Pages in both garden
  renderer modes. Desktop screenshots confirmed the full-viewport player
  view. The hosted build was `b9e59e9dea37a61d574220171c1282fe28a71b1a`.
  [Office](https://sufficit.github.io/sufficit-game-ultima/environments/?id=office)
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45721811259)
