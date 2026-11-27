# One Genius conversation per player and NPC

1. Complete. Trace player identity, transcript storage and the hosted adapter.
2. Complete. Use stable player and NPC identity and make Genius authoritative.
3. In progress. Verify browser and hosted continuity, then publish.

Acceptance requires the same conversation after reopening, reloading or
changing rooms, isolated NPC histories, and no parallel game transcript.
Existing Genius history for the browser player must remain accessible.

Release and unit checks passed. The hosted API test confirmed that a
Genius-owned request does not replay the legacy server transcript. Chromium
confirmed migration from an older Genius key, history sharing across a room
and peer-ID change, distinct NPC keys, and no separate game transcript.

Next action. Deploy the hosted adapter and confirm the public browser build.
