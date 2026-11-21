# Upload amortizado de modelos com budget por frame

## Objetivo e critérios

Eliminar travadas de frame ao subir os modelos das DLLs (árvores, pedras, tufos
de grama — 420+ instâncias hoje): o carregamento/upload para a GPU passa a
respeitar um **orçamento de milissegundos por frame** e um **limite de
carregamentos simultâneos** (em voo). O que não cabe no orçamento fica na fila
e entra nos frames seguintes.

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

1. [pendente] Instrumentar a carga atual: tempo de frame e total durante a
   entrada das DLLs (baseline).
2. [pendente] Implementar fila com budget por frame + limite em voo no
   carregador de modelos.
3. [pendente] Medir de novo no site publicado; fixar valores das constantes e
   registrar números antes/depois.
4. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1: baseline de frame time durante a carga dos modelos no site
publicado.
