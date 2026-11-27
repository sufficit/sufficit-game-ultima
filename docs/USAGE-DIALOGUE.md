# NPC dialogue

Approach the Garden Guardian or the Wanderer and open the conversation with
**E**, the controller's action button, or the on-screen **A** button. Select
an available topic to see the question and the character's response in the
transcript. You can then choose another topic or close the window.

The Guardian explains what to do in the garden, how to collect materials,
and how to light the entrance. The Wanderer offers directions. These topics
work without an AI model or a game API connection. Selecting a topic about
lighting displays instructions. Use **Iluminar o jardim** to request a world
edit. Authored topic turns stay in the open transcript. The Journal retains
free-text replies through the existing history pipeline.

## Add a topic

Add an entry to the corresponding character in
[DialogTopics.cs](../src/Npc/DialogTopics.cs). Keep its ID stable and unique
and use the player's locale for the question and reply. The
[dialogue brain](../src/Npc/DialogBrain.cs) checks the active character and
whether a free-text reply is pending before accepting the event. The
declarative window renders the available choices with Sufficit.Blazor.UI.

Run RulesSmoke for both characters' topic paths and `declarative-windows`
for keyboard and pointer selection in the browser.

## Qualify a local CPU model

With Python 3.11+, .NET 10 and Playwright Chromium available, run these
commands from the repository root.

```sh
dotnet publish src/Ultima.csproj -c Release
dotnet build app/UltimaServer.csproj -c Release
dotnet build tests/WorldCollisionE2E -c Release
python3 tests/WorldCollisionE2E/run-cpu-matrix.py \
  --cache /tmp/ultima-gguf-cache --output /tmp/ultima-cpu-results
```

The [manifest](../tests/WorldCollisionE2E/cpu-models.json) pins each GGUF
URL and SHA-256. Add an entry under `models` with a lowercase ID, a
pinned HTTPS `url` and a lowercase `sha256` to qualify a new artifact.
Use `--manifest` to point to a local manifest without editing the repository.
Pass IDs at the end of the command to test a subset. The runner verifies
cached files and downloads and serves weights on loopback. Use a dedicated
cache directory. It starts an empty disposable garden and a fresh
Chromium instance for every model. Exit code 0 means all models qualified,
1 denotes rejection or a model load failure, and 2 denotes an execution
error. It writes `results.json` and separate browser and API logs.

The underlying C# Playwright scenario is `guardian-cpu-tools`. To run it
with your own local servers, set `ULTIMA_SITE`, `ULTIMA_GARDEN_API`,
`ULTIMA_TOOL_CACHE` and `ULTIMA_CPU_TOOL_TRIAL`. The cache server must
serve `wllama.mjs`, `wllama.wasm` and `<model-id>.gguf` and allow
cross-origin requests if hosted separately from the client. Point
`ULTIMA_GARDEN_API` at a disposable loopback API instance with a new
`ULTIMA_DATA_DIR` and `NPC_SERVER_AI=off`.

The fixture substitutes asset URLs in the game adapter. It uses the real
browser runtime, tool schemas, confirmation UI and garden API. It clears
stored Genius history before independent cases and retains the color turn
for the follow-up. If a timed-out generation leaves the adapter busy, the
fixture reloads the tab before the next independent case. Negation runs last.
The first turn samples browser animation callback gaps over 50 ms
and, on Linux, Chromium proportional set size (PSS) in KiB. Browser
callback frequency measures page responsiveness. Actual draws may run at
a different rate. PSS includes Chromium, the scene and the model. Compare
runs on the same hardware and renderer.
