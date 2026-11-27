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

Use the C# Playwright scenario `guardian-cpu-tools` with a disposable garden.
The scenario confirms a real one-metre lantern edit and checks the resulting
map revision. The optional `ULTIMA_CPU_TOOL_TRIAL` mode also checks Portuguese
unit conversion, negation, color and a follow-up that references the prior
turn. It exits with a failure if the model misses any of those cases.

1. Publish the client with `dotnet publish src/Ultima.csproj -c Release` and
   serve the resulting `wwwroot` on loopback. Set `ULTIMA_SITE` to its URL.
2. Start the standalone API on loopback with `ULTIMA_DATA_DIR` pointing to a
   new empty directory and `NPC_SERVER_AI=off`. Set `ULTIMA_GARDEN_API` to its
   URL. Use a fresh directory for each model.
3. Set `ULTIMA_TOOL_CACHE` to a loopback static URL. Serve these files from
   it, preferably on the same origin as the game.

   | Fixture name | Published artifact |
   | --- | --- |
   | `wllama.mjs` | [wllama 3.13.0 ESM](https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/index.min.js) |
   | `wllama.wasm` | [wllama 3.13.0 WASM](https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/wasm/wllama.wasm) |
   | `qwen.gguf` | [Qwen2.5 0.5B Q4_K_M](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/blob/main/qwen2.5-0.5b-instruct-q4_k_m.gguf) |
   | `lfm350.gguf` | [LFM2.5-350M Q4_K_M](https://huggingface.co/LiquidAI/LFM2.5-350M-GGUF/blob/657e078c94084481950a2d555a941481f715536b/LFM2.5-350M-Q4_K_M.gguf) |

4. Run the scenario for each model with the same renderer and browser flags.
   Change `ULTIMA_CPU_TOOL_TRIAL` to `lfm350` to run the candidate.

   ```sh
   ULTIMA_CPU_TOOL_TRIAL=qwen dotnet run --project tests/WorldCollisionE2E -c Release -- guardian-cpu-tools
   ```

The fixture substitutes only asset URLs in the game adapter. It uses the
real browser runtime, tool schemas, dialogue history, confirmation UI and
garden API. The first turn logs latency, browser animation callback gaps
over 50 ms and, on Linux, sampled Chromium proportional set size (PSS) in
KiB. Callback frequency measures main-thread responsiveness. Actual draws
may run at a different rate. PSS includes Chromium, the running scene and
the model. Use the same hardware and scene settings when comparing runs.
