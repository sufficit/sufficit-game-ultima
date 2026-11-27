# Persistent player UUID

1. Complete. Map browser identity, game sessions, Genius, and telemetry.
2. Complete. Use one player UUID across those paths and preserve old data.
3. In progress. Validate and publish the browser and server.

Acceptance requires a stable UUID across tabs and reconnects, separate
transport and authentication identifiers, Genius history migration, and
telemetry correlation without exposing player UUIDs in the public summary.

Release, RulesSmoke, all 16 JavaScript tests and five server integration
scenarios passed. Chromium verified the UUID across a room and peer change,
its use in diagnostics and telemetry, and Genius history migration.

Next action. Promote the compatible API first and verify the public browser build.
