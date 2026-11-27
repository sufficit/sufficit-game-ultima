# NPC dialogue

Approach the Garden Guardian or the Wanderer and open the conversation with
**E**, the controller's action button, or the on-screen **A** button. Describe
your own request to the Guardian. An available model can answer, inspect
nearby placed objects, or propose changes. The pending preview comes from the
model's tool call and requires your confirmation before editing the world.
The Wanderer still offers authored directions.

## Conversation identity and storage

The embedded Genius runtime owns one conversation per browser player's UUID and
registered NPC ID. Reopening the window, reloading the game, or switching
rooms or local models all reuse that conversation. The conversation panel and
Journal read the same Genius transcript from IndexedDB. Different browser
profiles have independent local storage. No account synchronization is used.

When a server-side model answers, the browser sends the current request and
stores the result through Genius. The hosted API skips its legacy transcript
for those requests. Local conversation history stays in the browser.

## Select a local CPU model

Open **Painel**, expand **IA na CPU (sem placa de vídeo)** and choose a
model before pressing **Ativar IA na minha CPU**. The selection is saved
in the browser. Qwen is the default. **LFM2.5-350M** is an experimental
option with a 219 MiB download and support for native tool calls.

## Background Guardian replies

Close the conversation while the Guardian is thinking to return to the
game. Reopening the conversation shows the pending turn or its reply.
The game retains proposed changes for review and announces completion
when the game screen is open. The player confirms an edit from the Guardian's
conversation. Generation has no fixed game-imposed token budget or
deadline. The model, provider and browser keep their own runtime limits.
The browser must keep the game page open for the turn to continue.

A hosted server advertises `toolProposalsSupported` on
`GET /v1/game/garden/context`. Send `preview: true` with the message to
`POST /v1/game/garden/dialog` to receive structured actions for review.
The preview leaves the map unchanged. Confirm through the regular edit
route, which validates the actions against the current garden state.

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
