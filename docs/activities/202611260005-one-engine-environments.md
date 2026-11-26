# One engine for the garden and office

## Investigation

The office ran a second scene, input bridge, camera controller and
avatar renderer. The separate paths required duplicate changes even
after both used the same character rig and C# pose routines.

## Changes

- The garden and office now use [the same engine](../../src/wwwroot/js/game-core.js),
  input bridge, camera cycle, signed character models, and render loop.
  Both support OffscreenCanvas and the main-thread fallback.
- Authored scenes provide materials, geometry, lights, signs and spawn
  data through the [environment catalog](../../src/wwwroot/environments.json).
  The common C# movement sweep takes the office's
  [solid boxes and traversal bounds](../../engine/Ultima.Terrain/PlayerMovement.Scene.cs).
  The same camera and foot probes query the boxes in the rendered scene.
- Removed the separate office controller and renderer. The selector and
  environment route still open the office separately, with its mission
  board. The connection to the continuous map is a later milestone.

## Validation and delivery

- The terrain WebAssembly build, Release publish, RulesSmoke, and
  documentation link checks passed. The collision rules cover a thin wall,
  sliding along it, a doorway, a low step, and authored scene boundaries.
- Chromium tests covered all three cameras, aimed sidestepping and stopping,
  office traversal through a doorway, glass wall collision, keyboard,
  touch and gamepad input, and garden camera streaming and terrain steps.
  The office scenario also exercised the station preview and mission
  connection with an intercepted test API.
- The camera and office scenarios passed in both renderer modes on
  published revision `ae618cbd56e0f6d087f8a7c9b915358402f129ea`.
  [GitHub Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45746125803)
