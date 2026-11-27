# Preserve Guardian requests through failed turns

## Investigation

Two reports on Sufficit Run described an unfulfilled Guardian request and a
request that appeared to reset. One snapshot showed Gemma 3 270M ready on CPU
with WebGPU unavailable. The other captured CPU inference in progress. The
reports carry engine state but omit the conversation and the garden API outcome.
Neither report identifies the historical cause of its failure.

A delayed CPU response reproduced the draft disappearing as soon as the player
clicked Send. A failure while reading engine status could also leave the
dialogue waiting for a reply event that would never be submitted.

## Changes

- Keep submitted text visible, prevent duplicate submissions during inference,
  and preserve the draft after a failed turn so the player can retry. Clear it
  when the active turn completes successfully.
- Complete the conversation when engine status fails. Guard the completion
  against a canceled or replaced turn.
- Show refusals from the garden API and distinguish confirmed edits from an
  edit request that reports zero applied changes. If the server confirms a
  change and the local scene refresh fails, tell the player to reload the
  scene instead of repeating the mutation.

## Validation and delivery

- The new Chromium regression failed at the draft assertion on the previous
  build. It then passed in the main-thread and worker renderer paths and
  verified a delayed CPU response, empty output, engine status failure,
  retry with the same text, zero applied changes, and a confirmed edit.
  The scenario uses simulated inference and intercepted garden requests.
- Release publish, C# RulesSmoke, all 11 JavaScript tests, and all six
  ServerIntegration scenarios passed. A parallel browser run hit a focus
  assertion on a snackbar in the pre-existing Journal portion of the suite.
  The main-thread rerun passed on the same build.
- GitHub Pages served commit `65a429ff5207850212ae29694421806bed381ebe`.
  The full dialogue scenario passed on that public build in both renderer
  paths without writing to the production garden.
  [Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45930746012)
