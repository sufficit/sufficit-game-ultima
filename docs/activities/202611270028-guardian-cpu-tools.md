# Guardian native CPU tool pilot

## Investigation

The screenshot request “aumente a altura dessas luminarias 1m cada” fell
through to the local free-form planner. The previous relative shortcut only
recognized an unspecified height increase.

Tested GGUF Q4_K_M models in Chromium with the game's wllama 3.13.0,
one CPU thread and no GPU layers. These are single-run observations for a
small Portuguese task set. The first case includes warm-up and test runs
shared the machine, so the timings are indicative.

| Model | Download | Exact +1 m request | Other observations |
| --- | ---: | --- | --- |
| Gemma 3 270M | 241 MiB | No call | No native call on the five cases. |
| Qwen2.5 0.5B | 469 MiB | Native call with +1 m | Returned +1 for half a metre, +30 for a 30 cm reduction, and proposed edits for negation and a question. First request took about 28 s, other isolated calls about 5 s. |
| FunctionGemma 270M | 241 MiB | Tool tokens with +1 m | wllama returned the call as text. With unit examples and a stop token it got half a metre right, but returned +30 for the reduction and +0.5 for a negated request. |
| LFM2 1.2B Tool | 697 MiB | Native call with +1 m | Also got -30 cm and +25 cm right with unit examples. Returned -1 for a requested +0.5 m. First request took about 29 s, subsequent cases about 14–18 s. |

## Changes

- Add an opt-in CPU preset for Qwen2.5 0.5B native tool calling, reusing the
  existing GGUF download. The browser adapter forwards the tool schema and
  the model's actual `tool_calls`. Genius retains local dialogue history.
- The first tool proposes a signed height displacement in metres. The
  player sees the amount and can confirm or dismiss it. The small models
  made incorrect proposals even at temperature zero.
- The game API checks the player's session, distance to the Guardian, argument
  bounds and target dimensions. It updates up to two nearby lanterns atomically,
  preserving their positions and intensity. The client only confirms a change
  after the API responds.

## Validation and delivery

- The backend tests verified exact +1 m per lamp, authentication, and
  rejection without partial changes for invalid arguments and range failures.
  All six server scenarios passed. Release publish, RulesSmoke and all 11
  JavaScript tests passed.
- The real-model Chromium scenario ran the exact phrase, verified an unchanged
  map before confirmation, applied +1 m to both lanterns, and verified no
  change from the unconfirmed negated request. The main-thread run took about
  32 s from Send to the proposal. The tool's model and runtime downloads were
  served from a test cache, and inference ran in the browser.
- The same test passed in the worker at about 30 s and on the published
  Pages build `ebbd1e210373bb1e9df2ab40d38f997ded53339a` at about 33 s.
  All writes targeted disposable gardens.
  [Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45933964920)
- Sufficit Run release
  `163251c8c61ebaa3a2d2c12ddd13c8bc0558459dddf6fc3adfabb0f39bde4d37`
  passed health, manifest and anonymous-write rejection checks. Early
  promotions were rolled back while the route was missing from the manifest.
  The final rollout preserved world revision 57 and the existing data volume.
  The previous release remains available for rollback.
