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

1. [pendente] Protótipo em branch: worker mínimo com OffscreenCanvas renderizando
   a cena existente; aceitar ou rejeitar o custo do bundle com medidas.
2. [pendente] Migrar câmera, WASD e limites de mundo para o protocolo de mensagens
   (input no DOM → worker).
3. [pendente] Integrar `ai.js`/WebGPU ao device do worker; validar status da
   Guardiã e watchdog de travamento.
4. [pendente] Fallback para navegadores sem suporte; aviso no HUD e teste no site
   publicado.
5. [pendente] Publicar, validar o site ao vivo e converter este plano em atividade.

## Próxima ação

Criar o protótipo do checkpoint 1 em branch e medir: FPS, tempo de main thread
ociosa e bytes do bundle do worker.
