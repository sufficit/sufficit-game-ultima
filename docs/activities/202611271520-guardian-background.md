# Background Guardian turns

## Investigation

The browser tied pending Guardian turns to the modal lifetime. It also
requested fixed generation budgets and set an inference deadline. Hosted
dialogue used an API path capable of applying edits directly.

## Changes

Ordinary local Guardian requests use the runtime's uncapped prediction
setting. Hosted garden inference omits the token limit. The browser and
standalone server no longer impose a fixed deadline on those inference
requests. A turn continues while the player leaves the dialogue. Browser
session disposal cancels the turn.

A reply that arrives with the dialogue closed goes to the transcript.
The player can reopen the dialogue to review and confirm a retained
proposal. The hosted client requests preview mode only from servers that
advertise it. Existing direct API callers can still use the legacy
contract. The confirmation uses the existing validated edit endpoint.
The browser history persists replies. Pending work and actionable
proposals are held in memory while the page stays open.

## Validation and delivery

- Release publication, RulesSmoke and 14 JavaScript tests passed.
- All six standalone integration scenarios passed. The hosted fixture
  verifies that the request omits the generation cap and that a preview
  returns actions while preserving the map revision.
- Playwright passed in worker and main-thread render modes. A held turn
  survives close and reopen, stores its reply before the next reopen,
  and writes only after confirmation. The client sends the uncapped
  prediction setting to the engine.
- Real Qwen2.5 0.5B with wllama 3.13.0 returned a structured tool call
  after 93 seconds with the uncapped setting in an isolated Chromium
  inference probe. The probe did not exercise the game UI.
