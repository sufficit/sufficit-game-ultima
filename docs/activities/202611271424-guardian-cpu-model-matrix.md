# Guardian CPU model matrix

## Investigation

Compared the proposed local GGUFs with Qwen2.5 0.5B using the production
wllama 3.13.0 adapter on Linux Chromium with SwiftShader and the worker
renderer. The [manifest](../../tests/WorldCollisionE2E/cpu-models.json)
pins each weight file and SHA-256. FunctionGemma 270M and NanoAgent are
Q4_K_M conversions published by Unsloth and MatrixPortalX respectively.
The Distil Labs artifact identifies itself as F16. The NanoAgent GGUF
has no embedded chat template.

## Changes

The [matrix runner](../../tests/WorldCollisionE2E/run-cpu-matrix.py)
downloads and validates weights, launches an isolated API and browser
for each candidate, and writes per-model logs and an aggregate JSON
report. Add another artifact through the manifest or a local manifest.
The fixture checks that its expected URLs still exist in the shipped
CPU adapter before replacing them.

Independent cases run with empty Genius history. The color request
and its follow-up share history. If a timeout leaves the adapter busy,
the fixture reloads it before the next independent case. Only the
correct one-metre preview is confirmed. The suite checks the resulting
lamp heights and verifies that all unconfirmed turns preserve the
map revision.

## Validation and delivery

One initial run per model provided the first-turn timings below.
First-turn latency excludes model download and load. Chromium PSS
includes the garden, browser and model. The overlap between the Qwen
follow-up and the FunctionGemma recovery repeat occurred after both
models' initial first-turn measurements. The repeat has no comparative
performance figures.

| Artifact | Weight MB | First turn | Sampled peak PSS | Cases passed |
| --- | ---: | ---: | ---: | ---: |
| Qwen2.5 0.5B Q4_K_M | 491.4 | 43.28 s | 1.92 GiB | 2/5 |
| LFM2.5-350M Q4_K_M | 229.3 | 11.70 s | 1.40 GiB | 2/5 |
| FunctionGemma 270M Q4_K_M | 253.1 | 15.77 s | 1.45 GiB | 1/5 after repeat |
| NanoAgent 135M Q4_K_M | 105.5 | 6.13 s | 1.20 GiB | 1/5 |
| Distil Home Assistant FunctionGemma F16 | 542.9 | 14.98 s | 2.01 GiB | 1/5 |

| Artifact | Raise lanterns | Reduce by 30 cm | Set flower color | Color follow-up | Negation |
| --- | --- | --- | --- | --- | --- |
| Qwen | Applied after confirmation | Proposed setting a pine to 30 m | Correct proposal | Timeout | Timeout after reload |
| LFM2.5 | Applied after confirmation | Rejected string value and wrong operation | Rejected add operation for color | Proposed placing lanterns | No call |
| FunctionGemma | Text with tool tokens | Text | Text with tool tokens | Timeout | No call on repeat |
| NanoAgent | Text | Repeated request | Repeated request | Repeated prior reply | No call, suggested removal in text |
| Distil FunctionGemma | Tool tokens for a pine | Tool tokens for a pine | Text with tool tokens | Text with tool tokens | No call, emitted tool tokens as text |

The negation assertion checks for the absence of an executable call and
confirmation button. It does not judge the meaning of arbitrary reply
text. Both successful height confirmations raised two lanterns from
1.4 m to 2.4 m and kept the default intensity. The wrong or unconfirmed
proposals never changed the map revision. The Qwen run recovered the
adapter after the follow-up timeout and measured the negation independently.
FunctionGemma's original negation had no output after a timeout. Its repeat
completed the negation without a call. All five GGUFs loaded, and none
qualified across the full matrix. The production catalog still uses Qwen
for its previously verified capabilities.

The Playwright project compiled. RulesSmoke, 14 JavaScript tests, the
Release publish, and validation of custom manifest IDs and invalid
artifact URLs passed. Both matrix runs returned the model-rejection
status rather than an execution error.
