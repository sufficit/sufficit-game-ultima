# Guardian continuity with embedded Genius

Verify the live runtime and keep both scene context and failed requests
available for follow-up. Preserve the existing tool approval boundary.

1. Complete. Trace local and hosted inference and inspect production counters.
2. Complete. Resolve nearby context before local planning and persist visible failures.
3. In progress. Validate and publish the browser build.

Acceptance requires the next model call to receive a failed request and its
visible reply, and the first local planning call to receive player and nearby
object positions without relying on model tool selection.

Release, RulesSmoke and all 14 JavaScript tests passed. Chromium worker mode
verified the replay of failed dialogue and real positions in the first
planning request. The browser fallback and real-model probe are also running.

Next action. Publish the browser build and verify the live regression.
