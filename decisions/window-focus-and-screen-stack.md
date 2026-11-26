# Modal windows keep the focus and isolate the game behind them

## Context

The conversation and the journal opened as non-modal dialogs (`aria-modal="false`):
the focus stayed on the canvas, Tab reached the four controls behind the window, and
clicks on the page behind it still worked. On a phone, opening the journal from the
menu sheet left the sheet open on top of the journal, so every touch on the journal
area hit the sheet. The transient notices (announcement, proximity hint, new build)
also ignored the top safe area, while the menu button already respected it.
[Window component](../src/Windows/GameWindow.razor) · [Page](../src/Pages/Index.razor)

Measured on the published build `1da68d4` (2026-11-25): opening the conversation left
the focus on `#game-canvas`, and five Tab presses were needed to reach the window,
through four controls behind it. On 390×844, the journal opened from the menu sheet
stayed completely covered by the sheet.

## Decision

Treat the conversation and the journal as one modal level above the game.

- `GameWindow` declares `aria-modal="true"` and takes the focus when it opens.
- While a window is open, the page marks the status card, the game panel, the chat,
  the menu button, the menu backdrop and the update notice as `inert`, and takes the
  canvas out of the Tab order. The update notice also drops below the window layer
  while a window is open, so it never covers the window's controls; it is accepted
  or dismissed after the window closes. The touch buttons A and B stay active, so a phone
  keeps its close button.
- Closing the window returns the focus to the canvas, so the keyboard and the gamepad
  stay in the game. The engine focuses the canvas on start only when no window is
  open.
- Opening the conversation or the journal closes the phone menu sheet: the journal
  button sits in that sheet, and the window replaces it instead of opening underneath.
- The transient notices on phones respect `safe-area-inset-top`, like the menu button.
- The position, the FPS and the microphone level redraw the page only when their
  values are visible (status expanded or panel open). Collapsed, the 5 Hz tick still
  updates the state without re-rendering the whole page.

`inert` removes the elements from the focus order, from the accessibility tree and
from pointer hit-testing, so one attribute gives the three guarantees (focus, screen
readers, touch). The screen stack is two levels deep: the game, with its menu sheet
and game panel, and the window above it. A third level (a confirmation over a
window, for example) should introduce an explicit stack instead of new booleans.

## Consequences

- Keyboard and gamepad users land inside the window when it opens and leave it with
  Esc or the close button; they can no longer tab into the game behind it.
- Screen readers announce the window and do not reach the background while it is open.
- A test that needs a control behind a window must close the window first.
