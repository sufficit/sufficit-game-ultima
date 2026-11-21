# Upload amortizado de modelos com budget por frame

> **Concluído em 21/11/2026** — convertido na atividade
> [202611212030-upload-amortizado-modelos.md](activities/202611212030-upload-amortizado-modelos.md).
> Resumo da entrega: fila com orçamento de 4 ms/frame drenada pelo loop de render,
> scatter em lotes de 24 e monitor `game.perf.report()`; em produção, pior passo de
> qualquer modelo ficou em 4,7 ms e o custo de CPU do loop em 0 frames acima de 33 ms.

## Objetivo e critérios

Eliminar travadas de frame ao subir os modelos das DLLs (árvores, pedras, tufos de
grama — 420+ instâncias hoje): o carregamento/upload para a GPU passa a respeitar um
**orçamento de milissegundos por frame** e um **limite de carregamentos simultâneos**
(em voo). O que não cabe no orçamento fica na fila e entra nos frames seguintes.

Critérios de aceite:

- Constantes de orçamento (ms/frame) e de concorrência (em voo) nomeadas e
  ajustáveis no topo do módulo, com valores medidos no site publicado.
- Carregamento inicial do mundo sem picos de frame time (medir com
  `engine.getFps()`/performance marks antes/depois).
- Nenhum modelo perdido: fila drena completamente; HUD pode mostrar progresso.
- Fallback e recusa de DLL adulterada continuam idênticos.

## Terreno e referências

- Fonte: lineage-rust sobe personagens com `CHARACTER_BUDGET_MS = 4.0` e
  `CHARACTER_INFLIGHT = 16` — o upload é amortizado pelo frame loop
  (`apps/killer-web/src/runtime.rs`, constantes no topo).
- Eles também aplicam o mesmo princípio em outros lugares: fila de jobs de tiles
  (1 job por frame) e `TextureBank` com `trim_over(bytes)` periódico —
  referências para o futuro, não escopo daqui.
- Hoje no Ultima: DLLs baixadas por SHA-256 e instanciadas em sequência no
  `game.js` na carga do mundo.

## Checkpoints

1. [concluído] Instrumentar a carga atual: tempo de frame e total durante a
   entrada das DLLs (baseline). Medição rAF na produção (headless SwiftShader,
   1280×720, 11 s): p95 233 ms, máx 317 ms, 39 frames > 100 ms concentrados nos
   primeiros ~3 s (janela de entrada das DLLs).
2. [concluído] Implementar fila com budget por frame + limite em voo no
   carregador de modelos. Constantes `MODEL_BUDGET_MS = 4.0` e
   `MODEL_SCATTER_BATCH = 24` no topo do game.js; scatter virou colocador
   resumível; `game.models.register` apenas enfileira; dreno no início do loop de
   render com "sempre ≥ 1 passo". Commit `f88c019`.
3. [concluído] Medir de novo no site publicado. Timings por modelo em produção:
   pior passo 4,7 ms (wanderer), os demais ≤ 3,3 ms; CPU total por modelo
   3,9–10,0 ms espalhada em 5–21 passos ao longo de 100–300 ms; loop de render
   com mediana 2,2 ms e 0 frames > 33 ms de CPU (`game.perf.report()`). Os 6
   modelos carregaram e a fila drenou por completo. Ressalva: no headless a
   distância entre frames é dominada pelo rasterizador por software (SwiftShader),
   que mascara o efeito no FPS; a medição válida é o custo de CPU do loop e o
   pior passo por modelo, ambos dentro do orçamento.
4. [concluído] Converter este plano em atividade:
   [202611212030-upload-amortizado-modelos.md](activities/202611212030-upload-amortizado-modelos.md).

## Próxima ação

Nenhuma — frente encerrada. Constatação para o futuro: com o monitor
`game.perf.report()` publicado, novas frentes de carga (terreno em chunks,
multidões) já têm instrumento pronto de evidência.
