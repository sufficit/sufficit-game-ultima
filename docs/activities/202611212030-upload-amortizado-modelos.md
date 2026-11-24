# Upload amortizado de modelos: fila com orçamento de 4 ms/frame e scatter em lotes

## Investigação

A entrada das DLLs de modelos acontecia de forma síncrona: `game.models.register`
(construído por `ModelLoader` em C#) gerava texturas procedurais, malhas,
modificadores de vértice e TODO o espalhamento (64 árvores, 26 pedras por grupo,
420 tufos de grama) dentro de uma única chamada por modelo — o custo inteiro de um
modelo caía num frame só. A referência técnica veio do lineage-rust, que amortiza a
entrada de personagens com `CHARACTER_BUDGET_MS = 4.0` no frame loop
(`apps/killer-web/src/runtime.rs`), identificada na análise registrada em
[docs/README.md](../README.md).

Baseline medida na produção (Chromium headless, SwiftShader, 1280×720, 11 s,
128 frames): p95 233 ms, p99 286 ms, máx 317 ms, 39 frames > 100 ms — os piores
todos nos primeiros ~3 s, a janela de entrada das DLLs.

## Alterações

- `src/wwwroot/js/game.js` (commit `f88c019`):
  - `game.models.register` agora apenas valida e enfileira o descritor; o trabalho
    roda em passos drenados no início de cada frame pelo `drainModelQueue`, com
    `MODEL_BUDGET_MS = 4.0` de CPU por frame e execução garantida de ao menos um
    passo do item da frente.
  - Um material por passo, um grupo de malha por passo, registro no mundo como
    passo próprio (aí os personagens trocam o fallback), espalhamento em lotes de
    `MODEL_SCATTER_BATCH = 24` instâncias (`makeTreePlacer`/`makeInstancePlacer`
    resumíveis; pedras e grama nas mesmas 26/420 regras de antes).
  - Falha em um passo derruba somente o modelo dono do passo (log em
    `modelStatus.errors`); corrida placeholder×modelo real resolvida no passo de
    registro (FIFO: o real registrado primeiro vence, como antes).
  - Monitor `game.perf.report()`: custo de CPU do loop de render (mediana/p95/p99/
    máx dos últimos 600 frames, contagem > 33 ms) e `modelStatus` ganha `queue` e
    `timings` por modelo (cpuMs, passos, pior passo, enfileirado/concluído em ms).

## Validação e entrega

- Harness local (Babylon CDN + `game.init` com ponte .NET falsa, Playwright):
  modelo de prova com 2 materiais e 3 grupos drenou em 6 passos, 7,6 ms de CPU
  total, pior passo 3,4 ms, 0 frames > 33 ms; screenshot da cena renderizada em
  `/tmp/ultima-harness.png`.
- `dotnet publish src/Ultima.csproj -c Release` sem erros (13 s).
- Publicado no push `f88c019`; workflow "Publicar no GitHub Pages" concluído com
  sucesso (run 44961487080); `game.js` servido com `?v=f88c019e7d2…` confirma o
  build novo em produção (https://sufficit.github.io/sufficit-game-ultima/).
- Medição pós em produção (`game.perf.report()` + `models.status().timings`):
  os 6 modelos (árvores, pedras, grama e 3 personagens) carregaram sem erros e a
  fila drenou (queue 0). Pior passo de qualquer modelo: 4,7 ms (character.wanderer);
  demais ≤ 3,3 ms. CPU total por modelo 3,9–10,0 ms espalhada em 5–21 passos ao
  longo de 100–300 ms — antes, esse mesmo custo caía num único frame por modelo.
  Custo de CPU do loop: mediana 2,2 ms, 0 frames > 33 ms.
- Ressalva registrada: no headless sem GPU, a distância entre frames é dominada
  pelo rasterizador por software (SwiftShader), o que mascara o efeito no FPS
  medido por rAF; a evidência válida do orçamento é o custo de CPU do loop e o
  pior passo por modelo, ambos dentro do orçamento. Recusa de DLL adulterada e
  fallback de marcador não foram tocados (permanecem como em `86ada31`).

A carga de pacotes DLL é sequencial no cliente C#. O orçamento por quadro e o
lote de 24 se aplicam ao processamento dos descritores na fila do motor.
[Carregador de modelos](../../src/Models/ModelLoader.cs)

[Plano original e checkpoints no histórico do GitHub](https://github.com/sufficit/sufficit-game-ultima/blob/781180f5648fac40e896a7dbd847d0399ec7caa9/docs/PLAN-ASSET-UPLOAD-BUDGET.md)
