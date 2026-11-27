# Browser resource budget

## Investigation

An idle Garden scene with local AI and networking disabled used nearly twelve
CPU core equivalents on SwiftShader at 2560 × 1440. The software GPU process
accounted for almost all of that CPU time. Lowering resolution and disabling
shadows increased frame rate while the renderer continued to consume CPU.
Repeated map tile replacement also retained one material per update.

## Changes

The browser forwards initial visibility and visibility changes to both
renderer modes. The core stops its render loop while hidden, clears movement
input, and resets frame timing on resume. The session and assets stay available
for returning players.

Detected software renderers draw within a 1280 × 720 buffer at up to 8 FPS
when idle and 24 FPS during movement or camera input. Software rendering skips
shadow map allocation. The pacing policy applies to scene draws while visible
simulation and input sampling retain their existing scheduling.

Tile replacement and deletion release the owned material. Saved AI preferences
load the selected runtime when a player sends a dialogue turn or explicitly
activates local AI. Opening the game or conversation alone leaves models unloaded.

## Validation and delivery

The SwiftShader comparison used the Garden with a 1280 × 720 CSS viewport
at DPR 2. The table reports the median of three five-second idle windows per
build. Memory is summed proportional set size across Chromium processes.

| Measurement | Before | After |
| --- | ---: | ---: |
| CPU core equivalents | 11.86 | 4.68 |
| Chromium PSS (MiB) | 998 | 774 |
| P95 interval between scene draws (ms) | 273.3 | 134.0 |

The hidden-event probe drew zero frames and used 0.01 CPU core equivalents in
a five-second window. Visibility was injected in Chromium to test the game's
event handling. These short software-rendering samples do not establish a
long-session memory trend or reproduce a freeze on the player's hardware.

Release publish, RulesSmoke and JavaScript checks passed. Local Chromium passed
the keyboard, touch and gamepad scenario and the humanoid movement scenario in
the Garden and Office. With a mocked CPU backend, the first
dialogue message triggered a single on-demand model request. The browser resource scenario
passed locally and on GitHub Pages in worker and main-thread modes. It checks
pause and resume, software buffer and idle pacing, model startup, and material
disposal.

The public build is `049ee966ccecf066fd7b8f23bb5709039d1c082b`.
[GitHub Pages workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46007482741)
