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
  Document public contracts, units and coordinate systems, invariants, and
  compatibility assumptions beside the code. Keep player-facing text in its
  intended locale.
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

- Use only LLMs that can return structured tool calls with the exact inference
  adapter shipped or configured for the game. Validate the model/runtime pair
  with a real tool call before making it selectable. Game code validates
  arguments, permissions and effects before applying an action.

- Prefer C# for new logic and migrate existing code to C# wherever practical.
  Use Blazor or WebAssembly for client-side logic. Keep JavaScript focused on
  browser and rendering APIs that require it. Batch JavaScript–WebAssembly
  calls, compare frame cost in equivalent scenes, and validate the worker and
  main-thread fallback. See [ARCHITECTURE.md](ARCHITECTURE.md).
- Keep repository text files at or below 400 lines. The C# suite
  (`dotnet run --project tests/RulesSmoke -c Release`) and JavaScript suite
  (`node --test tests/*.test.mjs`) both enforce the policy. Each exception in
  [file-size-exceptions.json](scripts/file-size-exceptions.json) requires an
  exact path, a reason and a maximum line count. Remove an exception after
  its file reaches 400 lines or fewer.
- Split C# by responsibility with partial classes and `Type.Concern.cs`
  files when the code belongs to one type. Split large Razor files into
  focused components and code-behind files. For JavaScript, use focused modules
  with explicit interfaces and preserve both browser entry points.
  [Browser module boundaries](ARCHITECTURE.md#browser-module-boundaries)
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
