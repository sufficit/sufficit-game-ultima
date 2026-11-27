# Economy graphics for integrated GPUs

## Investigation

The Standard profile uses the canvas's device pixel ratio, a 2048 px
directional shadow map and a post-processing pipeline with MSAA, bloom and
sharpening. Accelerated integrated graphics use this hardware path. The
previous software-rendering protection responds to software renderer names.

## Changes

The Graphics menu offers Economy as a saved preference. Accelerated Economy
caps the buffer at 1280 × 720, uses a 512 px shadow map with lower filtering
quality, and skips the post-processing pipeline and native MSAA. It caps draws
at 30 FPS with movement or camera input and 15 FPS at rest. The browser's draw
schedule leaves the C# simulation cadence intact.

Active draw intervals and submission times feed a hysteresis policy. Sustained
pressure drops resolution through scales of 100%, 85%, and 70%. Stable active
windows gradually restore quality. The existing automatic software policy
retains its tighter pacing and skips shadows.

## Validation and delivery

Compared Standard and Economy in the Garden in Chromium WebGL2 using
the integrated Radeon GPU of an AMD Ryzen 9 9950X3D, with a 1280 × 720 CSS
viewport at DPR 2 and with networking and local AI disabled. Idle values
are medians of three five-second windows. Movement used one five-second window.

| Measurement | Standard | Economy |
| --- | ---: | ---: |
| Render buffer | 2560 × 1440 | 1280 × 720 |
| Directional shadow map | 2048 × 2048 | 512 × 512 |
| CPU core equivalents, idle | 0.24 | 0.11 |
| Chromium proportional memory, idle | 746 MiB | 745 MiB |
| Scene FPS, idle | 54.8 | 14.0 |
| Scene FPS, movement | 54.8 | 26.6 |

Injecting an extra 50 ms per active draw reduced the accelerated viewport to
1088 × 612 in both renderer modes. Unit checks cover gradual recovery and the
resolution floor. These samples measure CPU time and render cadence on one
integrated GPU. They leave player hardware stability and GPU utilization
unmeasured.

Release publish, RulesSmoke and JavaScript checks passed. Chromium verified
menu persistence after reload and the software fallback in the worker and on
the main thread, locally and on the published build. The public build is `8959544`.
[GitHub Pages workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46009114907)
