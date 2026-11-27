# Economy graphics profile

Provide a player-selectable profile for accelerated integrated graphics.
Limit GPU work while preserving gameplay simulation and automatic protection
for software renderers.

1. [complete] Add the profile, persist its selection, and apply the render budget.
2. [complete] Verify preference, render limits, and adaptive response. Compare with
   the Standard profile on available hardware, or record hardware limitations.
3. [in progress] Verify the published build.

Accept when the menu selects Economy, a reload restores the selection,
accelerated draws respect the resolution and frame budgets, and software
renderers retain their own stricter limits.

On Radeon integrated graphics with accelerated Chromium WebGL2, Garden idle
samples went from 0.24 to 0.11 median CPU core equivalents. The buffer went
from 2560 × 1440 to 1280 × 720 and the sun's shadow map from 2048 to 512 px.
Economy drew approximately 14 FPS at rest and 27 FPS during movement. An
injected frame-time stress test reduced the buffer further to 1088 × 612.
The software renderer and preference reload passed in both browser paths.

Next action. Verify the deployed build and archive the measurements.
