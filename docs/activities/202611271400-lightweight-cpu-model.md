# Lightweight CPU model qualification

## Investigation

Compared Liquid AI LFM2.5-350M and Qwen2.5 0.5B Instruct at Q4_K_M
with the game's wllama 3.13.0 CPU adapter. The artifact sizes were
229,312,224 and 491,400,032 bytes respectively. The local hashes matched
the publishers' metadata, and the ESM and WASM hashes matched the
distribution of wllama 3.13.0.

## Changes

Added an opt-in comparison to the C# Playwright scenario
`guardian-cpu-tools`. Run it against an empty, disposable loopback garden
with [the qualification setup](../USAGE-DIALOGUE.md#qualify-a-local-cpu-model).
The fixture replaces download URLs and retains inference, Genius history,
validation, confirmation and garden API execution. It logs per-turn latency
and rejects models that miss the specified structured calls. The first
turn also samples page animation callback gaps and Chromium process-tree
proportional set size (PSS).

LFM2.5-350M failed the qualification gate and stays outside the production
catalog. Qwen remains the existing CPU option for the capabilities
validated in the [previous tool qualification](../PLAN-LLM-TOOL-QUALIFICATION.md).

## Validation and delivery

Both models ran sequentially on Linux Chromium with SwiftShader in the
worker renderer. The published Release client and disposable API were
served on loopback. One run per model measures this setup.

| Scenario | LFM2.5-350M | Qwen2.5 0.5B |
| --- | --- | --- |
| Raise both lanterns by 1 m | Structured call, 11.20 s | Structured call, 44.85 s |
| Reduce lantern height by 30 cm | Text only, 3.31 s | Text only, 10.61 s |
| Negated request | No call, 97.01 s | Timed out at 120.11 s |
| Set flower color | Text only, 16.16 s | No tool output after the timeout |
| Repeat the color request from context | Text only, 8.10 s | No tool output after the timeout |

Each model raised both lantern poles from 1.4 m to 2.4 m after explicit
confirmation and preserved the default intensity. All unconfirmed
turns left the world revision unchanged. Neither model passed the full
suite. The two Qwen rows after the timeout measure behavior in the same
session and leave isolated color and follow-up accuracy undetermined.
The follow-up checks the model property and color from context. The API
contract used by the test selects nearby instances of a model.

The first turn peaked at 1,473,611 KiB PSS for LFM2.5 and 2,016,063 KiB
for Qwen. These are one-second samples of total Chromium memory with the
garden visible and the dialogue open. There were zero animation callback
gaps above 50 ms in either first turn. This is a page responsiveness
measurement. It gives no exact scene draw rate or integrated-GPU estimate.
First-turn latencies exclude the weight download and model load.

The Playwright project compiled, RulesSmoke passed, all 14 JavaScript
unit tests passed, and the Release client published locally. The two
opt-in browser trials returned a failed qualification verdict after
checking the map revisions and confirming zero browser page errors.
