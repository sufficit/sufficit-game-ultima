# Guardian quality telemetry

## Investigation

The dialogue history recorded the text of a turn and its source. It could not
separate inference failures, invalid tool calls, and edits that actually
changed the world, especially when the player closed the conversation while
the turn completed.

## Changes

- The browser measures total turn and confirmation times in milliseconds. The
  proposal retains the runtime and model of the original turn. Discarding or
  replacing a proposal records a dismissal with no latency.
- Events contain a random event ID, build, engine, model, outcome and duration.
  The collector rejects other fields. The browser keeps up to 256 queued events
  per configured server and attempts delivery every 30 seconds while open. The
  `telemetry=0` query parameter disables this collector for that page.
- The API acknowledges batches of up to 32 events. The latest 8,192 accepted
  events survive restart on the game data volume. Retries deduplicate by event
  ID within that window.
- The public aggregate endpoint groups counts and mean duration by build,
  engine, model and outcome. Use `GET /v1/analytics/guardian?build=<sha>` to
  compare one build. A proposal counts as a proposal, and an applied edit
  requires a confirmed server receipt. A plain reply can be appropriate for a
  question. Use the qualification suite for measuring intent accuracy.

## Validation and delivery

- Release builds passed for both Blazor and ASP.NET Core. RulesSmoke, all 14
  JavaScript tests, and the Garden, Diagnostics and Guardian Analytics API
  integration scenarios passed.
- Chromium exercised the worker and main-thread renderer paths with simulated
  inference, including an invalid call, a dismissed proposal, failed and
  successful application, structured payloads, and retry after unavailability
  and reload.
- The published client build `0bde26a47b43b37bc103d26d7b6fad95a7ff4560`
  passed the Chromium dialogue and telemetry scenario. The browser also sent
  a batch to a disposable API without intercepting the upload. The summary
  recorded one LFM2.5 reply from simulated inference.
- Sufficit Run release
  `3a1536692f6359a7dc0793f6061211df915422661f0505e19e10c55e5c5772a3`
  passed health, aggregate, CORS and invalid-payload checks. The shared world
  stayed on revision 59 with 19 placements, 32 edits and 2 tiles.
- The production summary had no samples at verification time.

[GitHub Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46052385672)
· [Public summary](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/analytics/guardian)
