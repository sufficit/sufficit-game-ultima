# Open-ended Guardian scene tools

Remove authored tasks and examples from the Guardian conversation. Let players
describe their own ideas and let the model inspect the scene before proposing
a generic edit.

1. Complete. Identify authored dialogue, scene data, and edit validation.
2. Complete. Add read-only spatial discovery and exact instance edits.
3. Complete. Remove preset Guardian actions and validate the conversation.
4. In progress. Publish and verify the browser and server.

Acceptance requires spatial observations to reach the model without changing
the world, exact changes to require confirmation, and the Guardian to present
a free-form conversation without suggested tasks.

Release, unit checks and the Garden and Guardian telemetry API integration
scenarios passed. Chromium exercised the hosted preview and a local tool-call
fixture that inspects one placement, proposes a change and commits only after
confirmation, in both renderer modes. The real LFM runtime produced and
applied a one-metre lamp adjustment using the revised tool definitions.

Next action. Publish the compatible server and the browser build, then verify both.
