# Characters API on Sufficit Run

## Investigation

The production manifest lacked the character body routes already supported by the
browser client. The Run workspace served the backend from a versioned release
directory and held the shared world state on its own persistent volume.

## Changes

Built the standalone backend from `app/` at commit
`6a5de1893a20f070811e90884224f5f8f43e41d8` using the Sufficit Run release job.
The job initially hit the ConfigMap quota. The oldest completed build's source
payload was backed up and removed after verifying that no running pod used it.
The successful build's source payload was removed after publication.

Promoted release `2dbd434673cbd47d45ef909e7165e36af486f2bc0b105c0b666918e34f5e1e0c`.
The switch used the Recreate strategy to keep a single writer on the shared
state. The deployment then returned to RollingUpdate with one ready replica.
Release `ccb41cdbb369566298afec1ca4ed7e0a6a686e212ac29c0a32c0f8a023f5eb91`
remains available for rollback.

## Validation and delivery

- Public health and manifest checks passed. The manifest lists GET and POST
  `/v1/game/characters/me/body`. CORS preflight succeeded and the anonymous
  character and mission requests returned 401.
- Two disposable sessions using the same display name verified profile isolation.
  Save, repeat save, invalid-input rejection, profile-key read, and persistence
  across leave and join passed.
- The public game in Chromium with SwiftShader loaded the saved height, mass,
  femur ratio, and waist circumference in the Garden, after a Garden reload,
  and in the Office. The browser used the main-thread render path and reported
  no page errors during the test. The synthetic profile was removed and its
  absence verified through the API afterward.
- The shared world stayed at revision 55 with 19 placements, 32 edits and 2
  tiles. The deployment retained the same `world-data` PVC.

See the [Characters API usage guide](../USAGE-API-CHARACTERS.md).
