# Guardian quality telemetry

Collect structured outcomes by model with a local buffer and a server
summary for comparing quality. The data must exclude conversation content.

1. Complete. Map existing logging, storage and Guardian outcomes.
2. Complete. Instrument the client and expose collection and aggregation.
3. In progress. Deliver the validated browser and server builds.

Acceptance requires browser events attributed to the selected model,
content-free batches, acknowledgment and retry, and a durable server summary.

Validation passed in Release. Browser tests covered the worker and main-thread
paths with simulated inference and a failed upload followed by a retry. The
server integration check verified filtering, persistence and deduplication
across restart. RulesSmoke and all 14 JavaScript tests passed.

Next action. Confirm the client build in GitHub Pages and the aggregate
endpoint on Sufficit Run.
