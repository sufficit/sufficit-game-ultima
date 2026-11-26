# Repository line limit and inline documentation

## Investigation

The repository had eleven text files above 400 lines. The existing C# guidance
described partial classes, while the shared JavaScript renderer had two classic
script entry points.

## Changes

- Documented the humanoid profile, measurement units, model descriptors,
  joint pivots and leg solver in English beside the code.
- Added the [400-line check](../../scripts/check-file-size.py) and
  [exact-path exceptions](../../scripts/file-size-exceptions.json). The check
  covers tracked files and new unignored files. It skips binary files and
  symlinks. Each exception records its current ceiling and the reason it
  exists. The check rejects stale exceptions.
- Added boundary and exception tests. GitHub Actions runs the tests and
  checks the repository on pushes to main and pull requests targeting main.
  A passing check is required before the Pages build runs.
- Recorded the [contributor rules](../../AGENTS.md) and
  [browser module boundaries](../../ARCHITECTURE.md#browser-module-boundaries).
  Existing large files remain in place under their recorded ceilings.

## Validation and delivery

The checker passed its four isolated Git repository tests and the full
repository scan. The Release build, terrain WASM build, RulesSmoke, and
JavaScript syntax check passed. The GitHub Actions gate, build and deployment
succeeded. The public build manifest matched commit
`74d4ef6be474315227a033de2a944dea9539854f`.
[GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45842477297)
