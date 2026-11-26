# Characters API and real Genius inference

## Investigation

The body profile was supplied by the environment catalog. Character
customization needed an owner-scoped API for players and future NPC tools.
The Genius checkpoint still needed real model evidence for reload continuity.

## Changes

- Added [character profile reads and partial updates](../USAGE-API-CHARACTERS.md)
  with species validation, private session credentials, and restart-safe
  persistence. Both environments load the profile before scene initialization.
- Added the [Characters API skill](../../.agents/skills/ultima-characters/SKILL.md)
  to the repository's API family index.
- Kept the standalone Run build self-contained. RulesSmoke verifies that its
  humanoid contracts mirror the client contracts.

## Validation and delivery

- Confirmed real CPU (Gemma 3 270M) and WebGPU (SmolLM2 135M) model responses
  on Pages build `d7df89c`. The previous turn appeared after a reload and was
  passed to both models. The GPU model did not answer the recall question
  accurately, so this verifies context delivery and inference.
- Passed 243 smoke checks, seven JavaScript tests, all six server integration
  scenarios, and Chromium profile and default-avatar tests in both renderer
  modes. A locked Release publish also passed using only the `app/` directory.
- Verified the updated client on Pages at
  `3eac1ada429e9bb7aff644a7043220cedce984f1`. The hosted Run manifest had yet
  to expose the new route. NPC tool integration remains in the
  [Genius plan](../PLAN-GENIUS-LOCAL.md).
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45886193406)
