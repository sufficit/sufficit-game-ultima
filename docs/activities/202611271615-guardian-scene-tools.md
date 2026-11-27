# Open-ended Guardian scene tools

## Investigation

The Guardian conversation offered authored topics, a fixed lighting command,
and buttons that filled the composer with specific garden requests. Local
models could propose edits, but their spatial information came from a fixed
list embedded in the planning prompt.

## Changes

- The Guardian now opens with a greeting and a free-form composer. Engine
  controls and confirmation or dismissal of a generated proposal remain
  available. The Wanderer's authored dialogue is unchanged.
- The `inspect_scene` garden tool reads the player's pose and up to eight
  placed objects by area and optional model ID. It returns effective model
  parameters and stable instance IDs. The local inference adapter can read
  once, pass the observation to the model, and then accept a reply or an
  edit proposal. Hosted planning receives a compact server-side snapshot.
- The `customize_model` tool can target an instance UID with the existing
  typed model properties. The regular edit pipeline validates scope, values,
  and permissions. The game commits AI proposals only after confirmation.
- Planning instructions describe the action contract without canned requests
  or sample edits. Naturally generated plants with no placement UID remain
  outside the instance tool.

## Validation and delivery

- Release publish, RulesSmoke, 14 JavaScript tests, and the Garden and
  Guardian telemetry server integration cases passed.
- Chromium passed on both rendering paths with the disposable API. The local
  model fixture inspected one fern by location, retained the map revision
  through the preview, and updated only the chosen fern after confirmation.
  The hosted provider fixture also required confirmation before editing.
- Real LFM2.5-350M applied a one-metre lantern adjustment after confirmation
  and still scored two of five in the standard Portuguese qualification.
- Production Sufficit Run release
  `c62ca90071540445043ccf48851da933226e0566bbc53d7a6207c25928997040`
  passed a read-only scene inspection. The map stayed at revision 59
  with 19 placements, 32 edits, and two tiles.
- In a separate real LFM2.5-350M location request, the model asked for player
  coordinates instead of calling `inspect_scene`. The map remained unchanged.
  The tool contract and read-then-propose flow passed with a model fixture.
- GitHub Pages served build `ae0d1fdcb873ed2ec0e9e665516fc67cf9a92543`.
  The published conversation regression passed.
  [Deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46054990913)

See the [garden API contract](../USAGE-API-JARDIM.md#model-discovery-and-native-tool-calls).
