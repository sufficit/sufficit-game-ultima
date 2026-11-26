# Modal windows keep the focus and isolate the game behind them

## Investigation

- Measured on published build `1da68d4` (2026-11-25) at 1280×720: opening
  the conversation left the focus on `#game-canvas`, and four controls behind
  the window came before it in the Tab order (`aria-modal="false"`).
- On 390×844, the journal opened from the phone menu sheet stayed under the
  sheet, so every touch on the journal area hit the sheet.
- The transient notices (announcement, proximity hint, new build) ignored
  `safe-area-inset-top`, while the menu button already respected it.
- The page re-rendered at 5 Hz for the position and the FPS, and on every
  audio analysis frame for the microphone level, even with the status card
  collapsed and the panel closed.

The design rules and the measurements are recorded in the
[decision](../../decisions/window-focus-and-screen-stack.md).

## Changes

- [GameWindow](../../src/Windows/GameWindow.razor) declares
  `aria-modal="true"` and takes the focus when it opens; the focus ring only
  shows for keyboard and gamepad focus.
- While a window is open, [the page](../../src/Pages/Index.razor) marks the
  status card, the game panel, the chat, the menu button, the menu backdrop
  and the update notice as `inert`, and takes the canvas out of the Tab
  order. Closing the window returns the focus to the canvas
  ([browser bridge](../../src/wwwroot/js/browser.js)).
- Opening the conversation or the journal closes the phone menu sheet, so the
  window replaces it instead of opening underneath.
- The new-build notice stays behind an open window: `inert`, and in the game
  layer instead of the top layer, so its buttons neither take the focus nor
  cover the window. It is accepted or dismissed after the window closes.
  [BrowserServices.cs](../../tests/WorldCollisionE2E/BrowserServices.cs) asserts
  both.
- The transient notices on phones respect the top safe area
  ([app.css](../../src/wwwroot/css/app.css)).
- Event-driven HUD: the position and the FPS redraw the page only with the
  status expanded; the microphone level, only with the panel or the menu
  open. Every field that stays visible keeps its own event: proximity, NPC
  decisions, chat, AI activity and announcements still re-render on change.
- Chromium scenarios: [DeclarativeWindows.cs](../../tests/WorldCollisionE2E/DeclarativeWindows.cs)
  asserts the modal role, the focus entering the window, the inert menu, Tab
  not escaping, the canvas leaving the Tab order and the focus returning to
  the canvas; [MobileLayout.cs](../../tests/WorldCollisionE2E/MobileLayout.cs)
  opens the journal from the menu sheet. The AI simulator is installed at page
  load, outside `aiCpu.init`.
- The stability check of the `input-devices` scenario
  ([InputDevices.cs](../../tests/WorldCollisionE2E/InputDevices.cs)) waits for
  twelve consecutive frames of the renderer that runs the simulation, without
  movement. It compared two positions twelve gamepad polls apart, which made
  it fail at random on the published build (see the live validation).
- To keep every file within the [line limit](202611261150-file-line-limit.md)
  that `main` gained meanwhile, the AI activity summary of the status card
  moved to [Index.Status.cs](../../src/Pages/Index.Status.cs), the window
  members of the page to [Index.Dialogue.cs](../../src/Pages/Index.Dialogue.cs)
  and the journal-from-menu assertions to
  [MobileLayout.Journal.cs](../../tests/WorldCollisionE2E/MobileLayout.Journal.cs).
  Only the place changed.

## Validation and delivery

All scenarios ran in headless Chromium with software rendering, one browser
at a time. The bench is this revision published without `wasm-tools`; the
control is `main` `5e65e96` published the same way; published is the live
GitHub Pages build (AOT). There were two rounds: the change alone on `main`
`5e65e96`, and the change merged with `main` `f053af8` (humanoid proportions
and the line limit), which is the published revision.

Scenarios of the change alone, on the bench:

| Scenario | Renderer | Result |
|---|---|---|
| `declarative-windows` | worker | passed, 90 s |
| `declarative-windows` | main thread | passed, 132 s (round before the update-notice fix) |
| `mobile-layout` | main thread, by design | passed, 153 s |
| `browser-services` | main thread | passed, 97 s |
| `browser-services` | worker | no verdict (see limitations) |

`mobile-layout` always runs on the main thread, like the legacy test it
replaces: with software rendering a frame takes up to a second on the main
thread, and the scenario waits by polling, never by frame. Earlier runs of
the same scenario passed in 138 s and 115 s.

Without the fix, the assertions fail: on the published build `5e65e96`,
`declarative-windows` stops at `DeclarativeWindows.cs` ("o Diário é uma
janela modal"); on the control build of `main`, `browser-services` stops at
"atrás do Diário: o aviso de nova versão fica inerte".

Regression against `main` `5e65e96`, change alone: every scenario that
fails on the bench also fails on the control build of `main`. The message is
the same, except for `collision`: the control stops at "chega junto ao corpo de
lado", a marginal check that the bench failed in one run and passed in another,
where it then stopped at the step over the log (see below). On the published
build (AOT), `collision` and `office` fail with a different message.

| Scenario | Bench | Control (`main`) | Published (`main`) |
|---|---|---|---|
| `hud-status` | passed, 80 s | — | — |
| `clock` | passed, 76 s | — | — |
| `dialog-log` | passed, 54 s | — | — |
| `local-preferences` | passed, 111 s | — | — |
| `input-devices` | failed, 63 s | failed, 72 s | passed, 79 s |
| `collision` | failed, 116 s | failed, 118 s | failed, 211 s |
| `office` | failed, 73 s | failed, 64 s | failed, 109 s |
| `garden` | failed, 96 s | failed, 98 s | — |
| `camera-views` | failed, 112 s | failed, 95 s | failed, 105 s |

The bench verdict of `camera-views` comes from the 08:00 UTC round on the same
bench build; the three later attempts were stopped by the memory guard of the
executor before reaching a verdict.

Known failures carried over from `main`: "cancelar o toque deixou o
analógico ativo" (`input-devices`, at random in every build; the cause is in
the stability check, see the live validation), "chega junto ao corpo de
lado" (`collision`), a 30 s timeout in the office (`office`),
"Apliquei as mudanças no jardim." matching two elements (`garden`) and
"Scroll out reaches the expanded limit in clear space" (`camera-views`).

`RulesSmoke` and the Release publish passed, and no OOM kill was recorded
during the rounds.

### Merged with `main` `f053af8`: the published revision

Models, assets, terrain, the Release publish, the browser test project,
`RulesSmoke` and the backend were rebuilt from the merged tree without
errors, and the line check passed.

| Scenario | Renderer | Result |
|---|---|---|
| `declarative-windows` | main thread | passed, 135 s |
| `mobile-layout` | main thread | passed, 172 s |
| `browser-services` | main thread | passed, 117 s |
| `hud-status` | main thread | passed, 84 s |
| `dialog-log` | worker | passed, 47 s |
| `local-preferences` | worker | passed, 104 s |
| `clock` | worker | passed, 102 s |
| `input-devices` | worker | failed, 75 s: "cancelar o toque deixou o analógico ativo" |
| `declarative-windows` | worker | passed, 60 s (fourth run; see limitations) |
| `collision` | worker | failed, 417 s: "a perna sobe sobre o tronco e o jogador volta ao chão" (run on the previous merge, with `main` `bdb152e`) |
| `collision` | main thread | passed, 410 s |

One run of `mobile-layout` stopped at "tablet: cena ativa" after the first
three views passed (203 s, anonymous memory peaking at 1602 MB). It was
launched without `ULTIMA_MODE=main`, but the scenario forces the main-thread
renderer in its own URL, so that only changes its reported mode label; the
same launch had passed in an earlier queue (135 s) and the repeat passed. The
cause was not determined. No OOM kill was recorded.

`collision` on the main thread, where the test reads the position from the
scene probe, passes the step over the log (end z = 3.345) and the whole
scenario. On the worker the test reads the position from the HUD text, which
has one decimal and changes with the engine tick (at most 5 Hz, on rendered
frames): it read 3.2 where the assertion asks for more than 3.2. The scenario
expands the status card before it starts, so the re-render gate of this change
is open and the text updates as it did before. The same message appeared on
one run of the change alone, whose control build stopped
at the earlier side check and never reached this one. The reading points to the
text being stale or rounded rather than to the step: the probe-based wait just
before had already seen the player beyond the log. This was not confirmed
against a control build of `f053af8`.

The change reaches `main` through the Git Data API and the GitHub Pages
workflow builds it; the live build is identified by `build.json`.

### Live validation on the published site

Every scenario compares the build declared by the page with `ULTIMA_BUILD`.

On `29c519a`:

| Scenario | Renderer | Result |
|---|---|---|
| `browser-services` | main thread | passed, 78 s |
| `mobile-layout` | main thread | passed, 124 s |
| `hud-status` | main thread | passed, 68 s |
| `declarative-windows` | worker | stopped by the memory guard at 57 s (no verdict); the repeat passed, 67 s |
| `input-devices` | worker | failed, 50 s: "cancelar o toque deixou o analógico ativo"; the repeat passed, 61 s. A third run did not start: the site already served `1802505` |

On `1802505` (same game sources), `declarative-windows` passed on the worker
in 69 s, and `input-devices` failed twice on the worker: "cancelar o toque
deixou o analógico ativo" (53 s) and "desconectar o controle deixou o
analógico ativo" (45 s).

The `input-devices` failure was therefore not limited to non-AOT builds, as
this record said before: it came at random on the published build too. The
cause is in the stability check, not in the game: the check read the
position, waited twelve gamepad polls (`requestAnimationFrame` on the main
thread) and read it again, while the simulation advances with the frames of
the renderer, which is the worker with `OffscreenCanvas` in the default
mode. The two clocks have no fixed relation: on the published build under
software rendering the stability windows of the corrected check (12 to 20
renderer frames) took 1.8 to 5.3 s on the worker. A window of twelve
main-thread polls can therefore close before the release reaches the
simulation or during the deceleration that follows it, and the distance
exceeds the 0.04 m limit. The old window was not instrumented; the evidence
is the code, the random failures on the same build, the four passes of the
corrected check and its negative control. No changed line of the game
scripts between `5e65e96` and `29c519a` touches the stick, the touch or the
delivery of input.

With the corrected check, on `7b9239b` (same game sources):

| Scenario | Renderer | Result |
|---|---|---|
| `input-devices` | worker | passed, 68 s, 69 s and 67 s |
| `input-devices` | main thread | passed, 74 s |
| `input-devices`, control without the touch cancel | worker | failed, 96 s: "cancelar o toque deixou o analógico ativo" (240 frames, 40 s, player still moving) |

The control is a temporary copy of the scenario that does not send the
`pointercancel`; it was not published. It shows that the corrected check
still rejects a stick that stays active. The stability windows took 12 to 20
frames.

The Release publish, `RulesSmoke` and the JavaScript suite passed on the
committed tree. The commit changes no game source, so the live scenarios
above were not repeated on it.

`main` repaired other scenarios of the browser suite meanwhile; see
[the test suite repair](202611261519-test-suite-repair.md).

Limitations:

- `declarative-windows` on the worker, the default renderer of the merged
  revision, needed four runs: the first three were stopped by the memory guard
  of the executor (1651–1653 MB of anonymous memory) before the end. The
  fourth passed in 60 s after the bench servers of the earlier rounds were
  removed (baseline 816 MB instead of 865–957 MB, peak 1590 MB). On the change
  alone it had passed on the worker (90 s).
- `backpack` (desktop and touch) has no verdict on the bench: it opens two
  game pages, and the 2 GB container of the executor stops it (anonymous
  memory above 1650 MB, 140 s in). Nothing after the first collection steps
  is verified. The same scenario against the published `main` is stopped by
  the same guard after the same elapsed time, so the limit is the container
  and not this change.
- Touch was emulated in Chromium, not tested on a phone.
