# LLM tool qualification

Require the exact model and inference adapter used by Ultima to support
structured tool calls. Keep model arguments under game-side validation.

1. [complete] Qualify the local catalog, migrate preferences, and require
   structured calls from hosted providers.
2. [complete] Verify local inference routing and rejection of text-only hosted
   responses. Check Release builds and public browser delivery.
3. [pending] Release the standalone server and qualify its configured hosted provider.

Do not expand the GPU catalog until its selected model and pinned runtime
pass the same tool-call checks. Keep direct game API authorization unchanged.

## Qualification notes

- The CPU catalog uses Qwen2.5 0.5B Instruct GGUF with wllama 3.13.0.
  Previous CPU selections resolve to this model. Loading remains on demand.
- WebLLM 0.2.89 lists selected Hermes 7B and 8B variants for its native
  function-calling API. Keep GPU inference unavailable until a model is
  validated with the same tools and fits the game's resource budget.
  [WebLLM supported models](https://github.com/mlc-ai/web-llm/blob/v0.2.89/src/config.ts#L70)
- Hosted inference requires a named call in `message.tool_calls`. The browser
  checks protocol support before using hosted dialogue. Deploy and validate
  the hosted server separately, including the configured model and provider.

## Validation

Release builds, RulesSmoke, JavaScript tests, all six isolated server
integration scenarios and the local dialogue window regression passed.
The hosted fixture returned a valid named call, malformed arguments and
a plain-text response. Only the valid call changed the world.

Chromium ran real Qwen inference in the worker against a disposable
garden. The model proposed a one-metre change for both lanterns. The
world changed only after confirmation, and an unconfirmed turn left it
intact. The published browser build `f28ee76` passed preference migration,
GPU exclusion and on-demand model startup in worker and main-thread modes.
[GitHub Pages delivery](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46032158177)

Next action. Deploy the standalone server separately and verify the actual
hosted model before enabling hosted inference in the game.
