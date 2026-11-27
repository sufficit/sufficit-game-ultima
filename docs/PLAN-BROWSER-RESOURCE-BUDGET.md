# Browser resource budget

Reduce the sustained cost of leaving Ultima open. Preserve visible gameplay,
input, network state, and explicit local AI controls.

1. [complete] Apply visibility suspension, an adaptive software-rendering
   budget, map material disposal, and lazy startup for remembered local AI.
2. [complete] Verify the browser lifecycle in both renderer modes, disposal, AI
   startup, and compare CPU and memory with the same SwiftShader baseline.
3. [in progress] Run Release checks and verify the GitHub Pages build.

Accept when an explicit visibility transition pauses and resumes the renderer,
the software policy caps buffers and draws without throttling C# movement ticks,
removed map tiles stop retaining materials, and saved preferences load local AI
only on demand. Record the benchmark conditions and device limits.

The SwiftShader Garden benchmark used a 1280 × 720 viewport at DPR 2, with
network and local AI off. Three five-second idle samples gave medians of
11.86 to 4.68 CPU core equivalents, 998 to 774 MiB PSS, and 273.3 to
134.0 ms p95 draw interval. The hidden-event probe drew zero frames and
used 0.01 CPU core equivalents in a five-second window.
The browser regression passed in worker and main-thread mode with a mocked
AI module. It exercised the visibility event and tile resource disposal.

Next action. Confirm the Release gates and the published build.
