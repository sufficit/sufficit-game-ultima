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

Checkpoint 2: extrair do game.js o protocolo de mensagens (init/input/comandos)
reaproveitando o worker do protótipo; a página do protótipo (branch
`worker-proto`) serve de teste A/B durante a migração.
