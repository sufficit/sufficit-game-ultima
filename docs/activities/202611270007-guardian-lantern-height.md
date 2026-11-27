# Apply height-only requests to garden lanterns

## Investigation

The player's screenshot showed the exact phrase “aumente a altura dessas
luminarias” reaching Genius local. The relative-edit shortcut only matched
a request for taller and brighter lanterns in the same sentence. The
height-only request therefore reached the general planner. The new API
regression reproduced that misrouting on the previous backend.

## Changes

The browser and backend share a bounded Portuguese command parser. The
garden editor adjusts height, intensity, or both according to the request.
Height adds 0.6 m to the pole on up to two nearby placed lanterns, respecting
the model limit. The same placements retain their positions and their
existing intensity. The combined request retains its original behavior.

## Validation and delivery

- All six server integration scenarios, C# RulesSmoke, the 11 JavaScript
  tests, and the Blazor Release publish passed.
- The isolated Garden Chromium scenario passed on the main thread and in
  the worker. The screenshot phrase skipped inference, updated the light
  positions in the scene, and preserved brightness. The integration tests
  also checked an intensity-only request.
- Sufficit Run published release
  `67f1a338431bd3eb01c29226998e42e151ced21dabef4fdaf975d2bd13e48fda`.
  Health, catalog, and authorization checks passed. The existing volume
  retained world revision 55, 19 placements, 32 edits, and two tiles.
  The preceding release remains available for rollback.
- Pages served commit `e3cbf9cc81960bc6e84ac07fc6167d7ad8ce314e`.
  The Garden scenario passed again with the live client and a disposable
  loopback backend. The regression made no edits to the production map.
  [Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45931963950)
