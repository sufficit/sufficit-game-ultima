# Engine em Web Worker com OffscreenCanvas

> **Concluído em 22/11/2026** — convertido na atividade
> [202611220130-motor-worker-offscreen.md](activities/202611220130-motor-worker-offscreen.md).
> Resumo da entrega: motor extraído para `game-core.js` (`UltimaCore.create(host)`, sem
> DOM), shell decide worker×fallback com recuperação automática, loader glTF no worker
> verificado pelo mesmo SHA-384 do SRI via blob, HUD marca o modo. Produção: worker com
> 6/6 modelos DLL + 6/6 GLB (450 instâncias) e fallback `?render=main` integral.

## Objetivo e critérios

Mover o loop de renderização Babylon.js (`src/wwwroot/js/game.js`, hoje na main
thread com `requestAnimationFrame`) para um Web Worker dedicado usando
`canvas.transferControlToOffscreen()`. A main thread fica só com a UI Blazor,
captura de input e mensagens.

Critérios de aceite:

- Mundo renderiza igual (chão, personagem, Guardiã, cenário das DLLs) com o loop
  dentro do worker; main thread sem trabalho de render.
- WASD/setas e tecla E continuam funcionando: input capturado no DOM e enviado
  ao worker por `postMessage` tipado.
- `engine.getGlInfo().renderer` ainda comprova GPU acelerada a partir do worker.
- A conversa da Guardiã (`ai.js`, WebLLM/WebGPU) opera no mesmo dispositivo sem
  regredir status nem watchdog.
- Fallback: navegador sem OffscreenCanvas/WebGPU mantém o caminho atual da main
  thread, com aviso no HUD.

## Terreno e referências

- Fonte: lineage-rust roda a engine inteira num worker; o DOM cria o worker,
  transfere o canvas e posta eventos de input.
  - `web/main.js` (worker + `transferControlToOffscreen` + postMessage com transfer)
  - `web/worker.js` (carga do wasm, criação do cliente, loop rAF dentro do worker)
  - `apps/killer-web/src/browser.rs` (surface sobre `OffscreenCanvas`)
- Atenção conhecida: Babylon.js no worker exige bundle do worker com a própria
  cópia da engine (worker com `type: "module"`); medir o custo do segundo bundle
  versus `importScripts`/bundle compartilhado antes de fixar a abordagem.
- A ponte C# (JSInterop em `src/GAME.JsInterop.cs`) continua falando com a main
  thread; a main thread repassa comandos ao worker.

## Checkpoints

1. [concluído] Protótipo em branch: worker mínimo com OffscreenCanvas renderizando;
   custo do bundle medido e **aceito**. Branch `worker-proto` (commit `d3eed90`,
   `src/wwwroot/js/game-worker.js` + `worker-proto.html`): Babylon 9.36 sobe no
   Web Worker via `importScripts` do mesmo CDN, mesma cena dos dois lados.
   Medidas (Chromium headless/SwiftShader): **FPS idêntico** (60 estável),
   **DynamicTexture OK no worker** (OffscreenCanvas 2D — pré-requisito das
   texturas procedurais), init do worker 24 ms, criação total 340 ms — os
   ~316 ms extras são fetch (cache HTTP) + parse/compile do bundle no contexto
   do worker, custo único por visita com RAM extra (~5–7 MB). Veredito:
   aceitável em troca da main thread livre; `attachControl` não serve no
   worker (input chega por mensagem, como planejado).
2. [concluído] Migrar câmera, WASD e limites de mundo para o protocolo de mensagens
   (input no DOM → worker). Commit `07ff4f2`: `game.js` virou shell (teclado, gamepad
   via rAF na main, analógicos virtuais e ponteiro capturados no DOM, entregues por
   `api.input`); câmera no worker por deltas manuais (≈0,008 rad/px, raio pela roda);
   `makeGrassTexture` com `OffscreenCanvas`; `game.init` só resolve quando o core
   avisa cena pronta (`onCoreReady`) — o C# registra modelos com a cena garantida.
3. [concluído] `ai.js`/`ai-cpu.js` seguem na main thread de forma independente
   (WebGPU/WASM próprios), coordenados pelos eventos da ponte — sem compartilhar
   device; diálogo da Guardiã validado no worker em produção com resposta e origem.
4. [concluído] Fallback: `?render=main` força; sem OffscreenCanvas/Worker cai direto;
   worker que falha no boot recarrega em modo main uma vez por sessão
   (`sessionStorage`); HUD mostra `· render em worker` no chip da GPU.
   Loader glTF no worker baixa, confere o MESMO SHA-384 do SRI (crypto.subtle) e
   importa por blob URL — a garantia de integridade foi preservada.
5. [concluído] Publicado no push `07ff4f2`; produção validada nos dois modos (série
   temporal do worker: 6/6 modelos, 6/6 GLB, 450/450 instâncias em ~4 s; fallback
   completo; OPFS com 12 acertos na 2ª visita). Atividade:
   [202611220130-motor-worker-offscreen.md](activities/202611220130-motor-worker-offscreen.md).

## Próxima ação

> **Decisão de 22/11/2026:** a migração (checkpoints 2–5) ficou **programada, não
> executada** — por coordenação, não por técnica. A frente de
> [Atividade de catálogo de modelos](activities/202611232303-catalogo-de-modelos-do-mundo.md) está ativa no mesmo `game.js`
> (módulo `game.glb`, fases 2–4 ainda por vir); refatorar o arquivo inteiro agora
> forçaria rebase massivo no trabalho concorrente. O protótipo (checkpoint 1)
> comprovou a viabilidade; o design abaixo deixa a execução pronta para a
> primeira janela em que a frente de assets estabilizar (ou em coordenação
> explícita com ela).

Design de migração validado pelo protótipo (branch `worker-proto`, commit
`d3eed90`), pronto para executar:

1. **`js/game-core.js`** — todo o motor (cena, terreno, fila de modelos, GLB,
   Andarilho) como `UltimaCore.create(ctx)`, sem DOM. `ctx = { canvas, mode,
   build, postEvent(nome, args), sendPose(x, z, ry), onInputMode(kind) }`.
   Ajustes conhecidos: `makeGrassTexture` troca `document.createElement('canvas')`
   por `new OffscreenCanvas` (fallback `typeof OffscreenCanvas`); input chega por
   métodos `api.keys/stick/pad/cameraInput/resize/clearInput`; `attachControl`
   só no modo main (no worker, a câmera recebe deltas manuais: alpha −= dx·0.008,
   beta += dy·0.008 com clamp, raio por roda ≈ `wheelDeltaPercentage`).
2. **`js/game.js` (shell)** — decide o modo: worker quando
   `canvas.transferControlToOffscreen` existir (a menos de `?render=main`);
   cria o worker, transfere o canvas, faz a ponte de mensagens
   (`event` → `dotnet.invokeMethodAsync`; `pose` → `game.net`/`game.live`;
   input do DOM → worker; comandos `game.*` → worker; diagnósticos
   `status/report` por request id assíncrono). Fallback: `UltimaCore.create`
   direto com os handlers de DOM atuais. `OnEngineReady` ganha sufixo
   ` · render em worker` no HUD (zero mudança em C#).
3. **`js/game-worker.js`** — `importScripts` do Babylon, `assets.js` e
   `game-core.js` (mesma pasta do worker); init recebe `{canvas, build}`;
   eventos saem por `postMessage`.
4. **`js/assets.js`** — `init(build)` aceita o build por parâmetro (no worker
   não há `<meta>`); OPFS funciona em worker (validado no protótipo via
   DynamicTexture/OffscreenCanvas 2D).
5. **Risco novo (do módulo GLB)**: o loader glTF é injetado por `<script>` com
   SRI — inexistente em worker. Solução proposta: no worker, `fetch` do loader
   vendorizado + verificação sha384 via `crypto.subtle` + `importScripts` de um
   blob URL (importScripts aceita blob), preservando a garantia de integridade.
   Prototipar isso é o primeiro passo do checkpoint 2.
6. **Validação** — script A/B com `?render=worker|main`: diálogo dos dois NPCs,
   6 modelos + assets GLB (450 instâncias), OPFS na 2ª visita, perf/report
   assíncrono; produção nos dois modos.
