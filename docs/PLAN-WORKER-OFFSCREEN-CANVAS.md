# Engine em Web Worker com OffscreenCanvas

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
2. [pendente] Migrar câmera, WASD e limites de mundo para o protocolo de mensagens
   (input no DOM → worker). Ponto de atenção encontrado: `makeGrassTexture` usa
   `document.createElement('canvas')` — no worker trocar por `new OffscreenCanvas`;
   o restante das texturas já usa `ImageData`/contexto da própria DynamicTexture.
3. [pendente] Integrar `ai.js`/WebGPU ao device do worker; validar status da
   Guardiã e watchdog de travamento. (Nota do protótipo: `ai.js` usa WebGPU na
   main thread de forma independente — a integração é de COORDENAÇÃO das
   mensagens, não de compartilhar o device.)
4. [pendente] Fallback para navegadores sem suporte; aviso no HUD e teste no site
   publicado.
5. [pendente] Publicar, validar o site ao vivo e converter este plano em atividade.

## Próxima ação

> **Decisão de 22/11/2026:** a migração (checkpoints 2–5) ficou **programada, não
> executada** — por coordenação, não por técnica. A frente de
> [PLAN-PLAYER-ASSETS.md](PLAN-PLAYER-ASSETS.md) está ativa no mesmo `game.js`
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
