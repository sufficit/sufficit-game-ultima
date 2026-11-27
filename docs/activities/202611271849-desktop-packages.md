# Portable desktop packages for Linux and Windows

## Investigation

Ultima's existing renderer needs browser WebAssembly, WebGL and worker
support. A desktop launcher can preserve those entry points and reuse the
same client, saved-state contracts, and optional shared-world API.

## Changes

- Added a self-contained C# launcher for Linux x64 and Windows x64. It
  serves the packaged client on loopback and opens a dedicated window
  using an installed Chromium-based browser. Closing that window stops
  the local server. The default browser is the fallback.
- The default loopback origin stays fixed so the desktop browser profile
  retains its UUID, preferences, and saves across upgrades.
- The packaging command builds the current client, packages its terrain
  runtime from a clean directory, verifies the bundled Babylon.js against
  the existing SHA-384 pin, and produces compressed archives with SHA-256
  checksums. The bundled client uses web revision
  `52bf947788e454a1d0658e5b8cb5cc91f8ee10cc`.

## Validation and delivery

- Release builds, RulesSmoke and all 16 JavaScript tests passed.
- Verified both archive checksums and launched the extracted executables
  from a different working directory. Both served the game, environment
  route, WebAssembly and Babylon.js on loopback. Requests with unexpected
  Host headers and unknown paths were rejected.
- Linux reached the game in a dedicated Chrome window and stopped the
  launcher on window close. Chromium passed the dialogue suite on the
  worker and main-thread renderers.
- The Windows PE x64 executable served the bundled game under Wine 10.
  The Windows browser launch path still needs a native Windows smoke test.

[Build and use the desktop packages](../USAGE-DESKTOP.md).
