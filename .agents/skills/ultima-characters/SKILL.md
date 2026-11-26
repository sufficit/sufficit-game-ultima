---
name: ultima-characters
description: Reads and updates the owning Ultima player's body profile through the Characters API. Use for authorized changes to dimensions, proportions, and colors.
---

# Ultima Characters API

Use the owning player's existing session and the same API base URL as the
game. Send the session token in the `X-Game-Token` header.

1. Read `GET /v1/game/characters/me/body` to find the saved overrides.
2. Resolve the current values and validate the requested changes using the
   [body parameter table](../../../docs/USAGE-ENVIRONMENTS.md#player-body).
3. Send only the changed fields to `POST /v1/game/characters/me/body`. Send a
   complete RGB object for each color. Set a field to `null` to reset it.
4. Confirm the returned profile before reporting a successful change.
   The player sees the new body the next time the scene loads.

Keep bearer credentials private. See the [Characters API](../../../docs/USAGE-API-CHARACTERS.md)
for session resume and error semantics.
