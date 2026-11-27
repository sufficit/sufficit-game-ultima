# Garden edits driven by model contracts

## Investigation

The previous pipeline coupled the Guardian to a lantern-only command and a
fixed native tool. The world already had a general UID-based customization
operation, but the native tool did not discover the model's editable fields.
The Qwen model also invented asset identifiers on a wide-open schema and
occasionally chose scale for a request explicitly stated in metres.

## Changes

- Remove the lantern command parser and the special adjustment method.
  Discover placed model contracts through the garden API. A generic
  `customize_model` tool selects a declared asset and parameter and sets or
  adds a value. It uses the same authoritative editor, scope, lock and journal
  as UID-based edits. The existing API also supports atomic edits across
  multiple parameters and known UIDs.
- Feed actual catalog property IDs to the model as choices. Explicit
  units narrow those choices. The player can confirm the resulting proposal
  before the server validates and applies it.
- Lantern contracts expose pole height, luminous flux in lm, and sRGB color.
  Pine contracts expose height in metres and tint. Flowers expose model tint.
  Colors accept a hex string or packed sRGB integer. Legacy light intensity
  remains supported for existing saves.
- Move instance rendering into a shared renderer module loaded in both browser
  modes. Explicit lumen values use Babylon's luminous-power mode, and tint
  variants reuse geometry and materials. The flower tint applies to the
  textured model as a whole.

## Validation and delivery

- All six server integration scenarios, C# RulesSmoke, the 11 JavaScript
  tests, and the Blazor Release publish passed.
- Chromium verified the API-to-scene path for lamp height, lumens, and light
  color, a nine-metre pine, and a flower material tint in the main-thread and
  worker renderers.
- The local native-tool probe selected the flower tint, pine height, and lamp
  luminous flux with the requested values. The all-model probe first selected
  scale for a request in metres, which motivated explicit unit filtering.
- Run release `e7b2a9df27198fa4af1de48df224bc4d17ec848e4612c50bdc384fe4142068a3`
  exposes both tool routes and serves the new model properties. Anonymous
  tool calls are rejected. The rollout preserved world revision 57 and its
  existing persistent volume.
- The filtered catalog passed the real Qwen CPU scenario with lanterns, a
  placed pine, and placed flowers in the same world. The confirmed +1 m
  proposal increased both lantern poles. An unconfirmed request left the map
  unchanged. The worker run passed locally. The main-thread run on Pages
  build `238f761bf76c68dd97d4a074ea94ecac045edd25` passed with an isolated
  backend and took about 46 s to generate the proposal.
  [Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45936194789)
