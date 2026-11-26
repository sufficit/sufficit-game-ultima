# Fine scroll adjustment for the close camera

## Investigation

The close third-person camera had a fixed distance of 2.15 m.
The garden already forwarded wheel input to the engine. The office's
walk input forwarded mouse movement only.

## Changes

- The [shared C# close camera](../../engine/Ultima.Terrain/PlayerCloseCamera.cs)
  adjusts the camera distance by 0.08 m per 120 units of wheel input.
  It eases into the new distance and limits the range to 1.85–2.50 m before
  collision is applied.
- The close camera remembers its distance while the player cycles views.
  The first-person view and elevated orbit keep their own behavior.

## Validation and delivery

- Terrain WebAssembly, Release publish and RulesSmoke passed. Tests
  covered easing, the near limit and preservation of the setting on reentry.
- Chromium with SwiftShader verified both scroll limits while the pointer
  was captured in the garden and office. Camera streaming and elevated
  orbit zoom passed locally. A native X11 wheel tick moved the close
  camera from 2.15 m to about 2.07 m without changing the heading.
- The camera-views scenario passed on published revision
  `7e285f774ca40ca4ddbd50d64205a67ea29eb6a6` with the worker and
  main-thread renderer.
  [GitHub Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45739610993)
