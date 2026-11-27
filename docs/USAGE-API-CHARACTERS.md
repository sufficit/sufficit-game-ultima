# Ultima Characters API

Use the player's game session to update their body. The route applies to the
character associated with the credential.

| Operation | Route | Credential |
| --- | --- | --- |
| Read the saved overrides | `GET /v1/game/characters/me/body` | `X-Game-Token` or `X-Character-Profile-Key` |
| Update body fields | `POST /v1/game/characters/me/body` | `X-Game-Token` |

Create a session with `POST /v1/game/sessions`. The response includes
`profileKey`, a persistent bearer secret for this character. Store it
privately. To resume the same character profile in a new session, send
`{"name":"Traveler","profileKey":"…"}`. The display name does not grant access.
The key travels only in a header or a JSON body. The browser stores the key
per API base URL. The server stores the profile under its configured data
directory, isolated per world when `ULTIMA_WORLD_ID` is set.

For an update, send a JSON object such as
`{"height":1.8,"femurRatio":0.54,"waistCircumference":0.92}`. Omitted fields
keep their previous overrides. Set an optional field to `null` to restore its
species default. Colors accept a complete RGB object. Invalid values and
unknown fields fail with HTTP 400 and leave the profile intact. Repeating the
same update returns the same profile. Refer to the
[body parameter table](USAGE-ENVIRONMENTS.md#player-body) for units and ranges.

The response wraps the saved overrides in `profile`. A character with no
saved overrides returns `{"profile":null}`. The Garden and Office fetch the
profile before booting the renderer. Reload the scene to apply an API change.
The catalog profile is the fallback when the saved profile is unavailable.

NPC tools can use the same operation with the owning player's session.

## Player identity

`POST /v1/game/sessions` accepts an optional `playerUuid` in UUID version 4
format and returns it to the caller. The browser generates one UUID per
browser profile and reuses it across rooms and connections. API clients that
supply a UUID resend it when creating another session. Legacy API clients
that omit it receive a stable identifier derived from their profile key.
The caller's authenticated `GET /v1/game/state` also returns it.

The UUID identifies a player for conversation storage and telemetry. Character
IDs still route poses to active avatars. Session tokens and profile keys still
protect game operations. Public presence and aggregate analytics do not expose
individual player UUIDs.
