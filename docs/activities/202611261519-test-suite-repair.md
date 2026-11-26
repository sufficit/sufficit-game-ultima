# Ultima test suite repair

## Investigation

The integration replay test captured the map before both player chunks had
finished crystallizing. The browser suite exposed inconsistent sampling during
the lateral approach and gait tests, an ambiguous garden dialog locator,
a graphics expectation that assumed GPU effects, and a remote performance
probe that referenced a variable outside its scope.

## Changes

- The replay test waits for both fixture chunks and reports the expected and
  actual values when they differ.
- Lateral collision checks sample position and separation in the same rendered
  frame. Gait measurements use a fixed 60 Hz simulation step in the test
  fixture. The collision and gait thresholds were retained.
- The garden test matches the exact reply returned by the disposable backend
  and reports provider setup errors. Its provider fixture returns HTTP 503 for
  the unavailable-provider request and supplies a path, lamp, and flowers for
  the garden edit request.
- The graphics test verifies the effects and shadow generator supported by the
  renderer. The remote-avatar performance probe stores measurement state at
  render-loop scope and surfaces startup errors.

## Validation and delivery

- Passed 241 C# smoke checks, seven JavaScript tests, and five server
  integration scenarios. The replay passed 18 additional runs.
- Passed all 24 local Chromium scenarios. Collision passed in worker and
  main-thread modes. Cinema graphics passed with SwiftShader and AMD Vulkan.
  The two performance probes used the same local build for both inputs to
  validate the instrumentation and repeatability.
- The Release build and GitHub Actions passed. The public Pages manifest
  matched `87f8fb66dbb7c38b031fd533b31ac015f9428e9e`.
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45874653640)
