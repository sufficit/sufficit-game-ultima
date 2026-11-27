# One Genius conversation per player and NPC

## Investigation

Local conversation IDs included the room and the network peer, so moving to
another room or opening another tab could fork a player's history. The game
also kept a separate browser transcript for the Journal.

## Changes

- The embedded Genius transcript uses the browser's stable player ID and the
  registered NPC ID. Room, tab peer ID and inference engine are independent of
  the conversation. On first use, a matching legacy Genius snapshot can seed
  the canonical record. If several match, the highest revision is selected.
  Original records remain on disk.
- Both the conversation panel and Journal read the Genius transcript.
  Authored NPC turns, tool receipts, hosted replies and errors enter the same
  transcript when the embedded runtime has not already recorded the result.
  The old game transcript is no longer read or updated.
- Hosted requests in the game send only the current message. The server skips
  its legacy dialogue transcript for Genius-owned requests. Browser profiles
  remain isolated, and there is no cross-device history synchronization.

## Validation and delivery

- Release publish, server build, RulesSmoke and all 14 JavaScript tests passed.
  The Garden and Guardian telemetry API integration scenarios passed,
  including the hosted provider's stateless request path.
- Chromium passed in both renderer modes. The tests verified older Genius
  history adoption, dialog and Journal replay after changes to room and peer
  ID, NPC isolation, background replies, and confirmation without a
  parallel browser transcript.
- Sufficit Run release
  `0326a8b40c3fac6732b95b86f27b53d064714ef694c82af45e5327619717e854`
  passed public health and telemetry checks. The map stayed at revision 59
  with 19 placements, 32 edits, and two tiles.
- GitHub Pages served build `4d7f5ff1342a73b565fce91c400773e23cabb156`.
  The published dialogue regression passed in Chromium.
  [Deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46069490890)
