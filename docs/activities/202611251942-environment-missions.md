# Data-driven environments and missions

## Investigation

The garden renderer and API preceded the environment catalog. The office
prototype originally encoded its identity, stations, and geometry in code.
The authoring requirement moved those content choices into a catalog, with
the runtime responsible for validation and rendering.

## Changes

- [Environment catalog](../../src/wwwroot/environments.json) provides the
  garden and office as records. Terrain bounds and starting positions for the
  player and the two NPCs come from the terrain record. Object scenes use
  box geometry, named stations, a camera, and a background defined as data.
- The environment selector reuses Sufficit.Blazor.UI. The office example uses
  desks and a central aisle as landmarks. A single mission panel accompanies
  the 3D scene. This follows the existing Game Panel and the Refero guidance
  on card density. The object renderer contains no environment names.
- The C# mission API scopes progress by environment ID and accepts new
  definitions through an author key. Agents use an agent key to report
  work. The persisted store deduplicates retries and bounds definitions
  and progress. [Authoring and API guide](../USAGE-ENVIRONMENTS.md)

## Validation and delivery

- Release client and server builds and RulesSmoke passed. The integration
  suite verified role checks, event applicability, four concurrent retries,
  progress across restarts, and isolation when two environments share a
  mission ID. The garden API suite passed.
- Chromium opened a third environment injected solely as catalog data.
  The office opened without its API, displayed simulated mission progress,
  and switched back to the garden. The browser scenario passed locally
  and on GitHub Pages, including the garden's worker and main-thread modes.
  Camera streaming and preferences passed locally.
- GitHub Pages served `2ae8ba9e78847f5e35308e3a849b254592eb38f7` and the
  direct environment route returned HTTP 200.
  [Production office](https://sufficit.github.io/sufficit-game-ultima/environments/?id=office)
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45715124783)
- The mission backend was verified with a disposable local API. The hosted
  API still returned 404 for the new route. The pilot uses a published catalog,
  and the legacy shared garden retains its existing server rules.
