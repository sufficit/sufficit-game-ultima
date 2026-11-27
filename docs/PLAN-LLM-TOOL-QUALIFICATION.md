# LLM tool qualification

Require the exact model and inference adapter used by Ultima to support
structured tool calls. Keep model arguments under game-side validation.

1. [complete] Qualify the local catalog, migrate preferences, and require
   structured calls from hosted providers.
2. [in progress] Verify local inference routing and rejection of text-only hosted
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

Next action. Verify the final browser release. Keep hosted inference gated by
the server protocol until the hosted deployment and model are verified.
