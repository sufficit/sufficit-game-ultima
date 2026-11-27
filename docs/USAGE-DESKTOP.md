# Ultima on Linux and Windows

The portable desktop packages target 64-bit x86 computers and include the
Ultima client, its renderer and the .NET runtime. Extract the entire archive
before opening the executable. Keep the `wwwroot` folder beside it.

| Platform | Executable |
| --- | --- |
| Linux x64 | `Ultima.Desktop` |
| Windows x64 | `Ultima.Desktop.exe` |

The launcher opens a game window through an installed Chrome, Chromium or
Edge. If none is available, it opens the default browser. The browser must
support WebGL. Closing the dedicated game window stops the local server.
When the default browser opens as a fallback, close the launcher or press
Ctrl+C in its console to stop the local server.

The client uses the existing multiplayer and shared-world services when
available. Run the executable with `--offline` to turn off the shared-world
connection, multiplayer, and automatic game telemetry. The optional local
AI requires its model download on first use. The desktop game has its own
browser storage at `http://127.0.0.1:48757`, so its player UUID and browser
saves persist across desktop upgrades. The web version has a separate origin.

If the default port is unavailable, `--port PORT` chooses another. Different
ports have independent browser storage. `--serve-only` prints the local URL
without opening a window. `--port 0` is useful for automated tests with
disposable browser storage.

## Build the packages

Run on Linux with the .NET 10 SDK, the `wasm-tools` workload, Git, curl,
OpenSSL, tar and zip. Both x64 packages are built from the same web client.

```bash
bash scripts/package-desktop.sh
```

The optional argument sets the output directory. By default the packages
and SHA-256 files go to `publish/desktop`. The build verifies the bundled
rendering script against its pinned digest before putting it in the
package.
