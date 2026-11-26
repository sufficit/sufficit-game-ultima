# Humanoid body profiles

## Investigation

The avatar used a fixed 1.75 m body. The model, animated rig, camera, and
collision each derived dimensions independently. The
[species and body design](../../decisions/humanoid-species-and-body.md)
defines anatomy shared by humanoid species, with Homo sapiens as the first
implementation.

## Changes

- Added the C# `Humanoid` base class and `HomoSapiens` species. The
  [body profile](../../models/Ultima.Models.Core/HumanoidBody.cs) resolves
  defaults, validates overrides, and supplies measured proportions.
- Added optional [environment catalog configuration](../USAGE-ENVIRONMENTS.md#player-body)
  for height, mass, build, leg proportions, face width, ear size, and skin,
  eye, and hair colors. Both environments use the same local avatar profile.
- Used the resolved dimensions for geometry, rig, stance, stride, camera,
  and collision. The browser receives the rig and body data at scene startup.
  Profiles with omitted fields use the existing avatar defaults.
- The profile stores mass for future force and inertia rules. The new
  profile applies to the local avatar through the catalog. NPCs, remote
  avatars, and clothing continue to use their existing presets.

## Validation and delivery

- Built the signed model packages and the terrain WASM module, published the
  Blazor Release build, and passed RulesSmoke, including range validation
  and clearance checks.
- Exercised the garden and office on Chromium with SwiftShader in worker
  and main-thread modes. The 2.02 m test profile exercised geometry,
  materials, joint placement, first-person eye height, and office wall
  clearance. The local regressions covered the default camera modes, stepping
  over obstacles, and traversing the office.
- Verified the
  [published build](https://sufficit.github.io/sufficit-game-ultima/build.json)
  at commit `4af96e6d3aa461c2faf8f779e43c18377cda09e8`. The humanoid body
  test passed for both environments in worker and main-thread modes. The
  camera regression passed in worker mode on the same build.
  [GitHub Pages run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45833591245)
