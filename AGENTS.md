# Agent instructions for Ultima

Ultima is an open-source C# and Blazor WebAssembly game. Babylon.js renders its
3D world through WebGL or WebGPU. See [PROJECT.md](PROJECT.md) for the game
overview and [README.md](README.md) for the repository map.

## Build and validation

- Use the .NET 10 SDK. Run `dotnet publish src/Ultima.csproj -c Release`
  successfully before committing code.
- A push to `main` triggers GitHub Pages. Always verify the live build before
  reporting a deployment. Use the [production game](https://sufficit.github.io/sufficit-game-ultima/)
  in player-facing reports.
- Model assemblies under `models/` are verified by SHA-256 at runtime. Preserve
  the verification and the marker fallback.
- Run the relevant suites under `tests/`, including RulesSmoke when game
  behavior changes.

## Documentation and plans

- Write all repository documentation and inline code comments in English.
  Keep player-facing text in its intended locale.
- Put execution plans in `docs/PLAN-*.md` and link them from
  [docs/README.md](docs/README.md). Give each plan an objective, acceptance
  criteria, numbered checkpoints with status, and the next action. Reuse the
  plan for the existing workstream and update it as checkpoints change.
- Keep a plan open while any checkpoint is pending, blocked, or unverified.
  Once every checkpoint is verified, convert the plan into
  `docs/activities/YYYYMMDDHHmm-description.md` using America/Sao_Paulo time.
  Preserve decisions, evidence, delivery references, and limitations.
  Remove the completed plan, update all links including the documentation
  index, and verify the files and diff before committing. Leave plans from
  other sessions open until their own acceptance criteria are verified.
- Use **Investigation**, **Changes**, and **Validation and delivery** in
  activity files. Store historical design decisions in `decisions/` and
  record a new decision when revising one.

## Code and interface

- Prefer C# for new logic and migrate existing code to C# wherever practical.
  Use Blazor or WebAssembly for client-side logic. Keep JavaScript focused on
  browser and rendering APIs that require it. Batch JavaScript–WebAssembly
  calls, compare frame cost in equivalent scenes, and validate the worker and
  main-thread fallback. See [ARCHITECTURE.md](ARCHITECTURE.md).
- Aim for at most 400 lines per source file. Split by responsibility, using
  partial classes and `Type.Concern.cs` files when the code belongs to one
  type. Split large Razor components with focused code-behind files.
- The ASP.NET Core API in `app/` is optional. The game must start
  independently of it. The previous Python implementation lives in
  `app-python/`.
- Use Sufficit.Blazor.UI for panels, buttons, fields, selectors, and switches.
  Use the version pinned in the project. Keep control state in Blazor and
  pass it to the engine through interop. Use the World Workshop and Game
  Panel in `src/Pages/Index.razor` as references. The current expansion-panel
  `Subtitle` parameter joins the title and subtitle, so omit it.
  [Interface design history](docs/activities/202611230037-menus-sufficit-blazor-ui.md)

## Delivery

- Keep commits on `main` focused and descriptive. Preserve parallel work.
- The maintainer authorizes publication of technical changes to Ultima
  when the production site can be verified.
