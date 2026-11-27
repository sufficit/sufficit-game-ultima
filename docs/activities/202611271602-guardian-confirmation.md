# Guardian confirmation and follow-up context

## Investigation

The reported transcript showed a proposed pine width change followed by “faça”
and a different proposed value. Editing the text box had already cleared the
pending proposal. The send handler then routed “faça” through inference.
The Guardian's local inference history also contained proposals without the
API receipts from their application.

## Changes

- Typing and ordinary follow-up turns retain the pending proposal until it is
  applied, dismissed, or replaced by a new proposal. The dialogue displays
  the pending preview beside its actions when a different reply is showing.
- A standalone “faça” or “pode aplicar” uses the original tool payload. A bare
  “sim” confirms only as a direct response to the preview. The proposal is
  consumed before the request, so a repeated confirmation cannot write again.
  Longer requests go through inference and remain subject to confirmation.
- The local model receives the pending preview and recent conversation. The
  next turn can also see the confirmation and the actual application result
  stored by the embedded Genius runtime.

## Validation and delivery

- Release publish, browser test compilation, RulesSmoke, 14 JavaScript tests,
  and the Garden and Guardian telemetry API integration scenarios passed.
- Chromium passed with the worker and main-thread renderers. The regression
  checks pending state after a follow-up, the exact POST payload, suppression
  of repeated writes, and receipt replay in the next model turn.
- LFM2.5-350M generated a one-metre lamp adjustment on a disposable world.
  Typing “faça” increased both poles from 1.4 m to 2.4 m with no extra model
  invocation. The wider model qualification remained at two of five cases.
  The 30 cm conversion, color command, and reference to other flowers failed.
- GitHub Pages served build `b4231381e8c333788a378376597b5e7529590858`.
  The published dialogue regression passed in Chromium.
  [Deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46053457727)
