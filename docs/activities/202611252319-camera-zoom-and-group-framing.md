# Expanded camera zoom and group framing

## Investigation

Close third person had a 0.65 m adjustment range. The default near framing
works for direct control. The player requested more room to zoom out and
clarified that a future group mode should control up to five characters.

## Changes

- The [C# close camera](../../engine/Ultima.Terrain/PlayerCloseCamera.cs)
  now uses 1.85–3.80 m, tripling the range to 1.95 m. The 2.15 m default
  and 0.08 m scroll step remain. Camera collision still limits distance.
- The [design decision](../../decisions/camera-framing-and-control-scope.md)
  defines the current single-character control and the future group mode.
  The elevated view will frame the selection, and the closer views will
  follow the active member. The player guide and architecture link to
  the decision.

## Validation and delivery

- The terrain WebAssembly build, Release publish, RulesSmoke and
  documentation link checks passed.
- Chromium verified the range in the garden and office in both renderer
  modes. The office camera retracted at the reception wall and restored
  the requested distance in the clear aisle. The garden camera-streaming
  regression passed on the worker.
- The camera-views scenario passed in both renderer modes on published
  revision `4ae76ab38549b4b40f64e5328e98b627cb529f2b`.
  [GitHub Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45741371844)
