# Guardian continuity with embedded Genius

## Investigation

The browser adapter uses the pinned `Sufficit.AI.Genius.Embedded` package for
the local conversation runtime and IndexedDB for its turns. The browser
model generates the output. Hosted garden dialogue instead uses the game's
server-side provider. Production counters showed four LFM2.5 CPU replies.

A failed model call or rejected tool output could leave a visible reply in
the Journal without adding that exchange to the embedded transcript. Spatial
facts only entered local inference after the model explicitly requested them.

## Changes

- Keep the player's request and the visible failure reply in the embedded
  transcript when a local generation or tool turn fails. A concurrent
  transition conflict keeps the embedded runtime's existing revision checks.
- Supply nearby placement coordinates and the player pose to general planning
  turns on a compatible server. Targeted inspection remains available. The
  initial read is optional if the API cannot answer. Tool proposals still
  require player confirmation before any write.

## Validation and delivery

- Release publish, RulesSmoke, JavaScript tests and the Chromium conversation
  and garden scenarios passed on both rendering paths. Tests assert that
  model input contains failed exchanges and real positions, and that the
  world remains unchanged until confirmation.
- A two-turn LFM2.5 browser probe confirmed real scene data and history replay
  even after the first output was rejected. The follow-up confused a pine
  property with a lantern. Neither turn changed the map.
- GitHub Pages served build `5d3285ac17f8cb8fc7db6875b9db9a798147129d`
  and the published conversation regression passed.
  [Deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46066623419)
