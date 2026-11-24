# Câmera e animação por quadro em C# no navegador

## Investigação

O build `af7df4f` fazia seis chamadas de JavaScript ao WASM para calcular a
pose e os pés de um personagem articulado no piso. A câmera recalculava a
órbita e consultava a geometria a cada quadro, mesmo com o raio parado. Foi
medida a referência com a clareira carregada e renderização no worker.

## Alterações

- `PlayerCamera` mantém zoom e raio orbital, calcula a resposta aos controles e
  amortece a recuperação após uma colisão. O Babylon.js consulta a geometria
  visível e aplica o limite encontrado.
- A pose no piso e o avanço da ação são resolvidos em uma chamada C# por
  personagem. Uma segunda resolução corrige os pés do jogador somente quando
  há apoio de uma malha acima do solo.
- A câmera reutiliza a órbita estabilizada e reaproveita a consulta de colisão
  com raio e revisão geométrica idênticos. Chunks, edições de terreno, tetos e
  mudanças no conjunto de obstáculos ativos invalidam a consulta.
- A arquitetura e o `AGENTS.md` orientam a comparação do custo por quadro nas
  próximas migrações e a validação nos dois modos de execução.

## Validação e entrega

- Build Release de `Ultima.Terrain`, publish Release de `Ultima` e 94 casos do
  executável `RulesSmoke` concluídos.
- Câmara, streaming, colisão, apoio dos pés, coleta e túneis passaram em worker
  e `?render=main` no Chromium. O teste de travessia do tronco agora aguarda a
  distância esperada, com limite de tempo, e continua verificando passo e apoio.
- Comparação de 600 quadros por build com `game.perf.report()` no worker,
  Chromium com SwiftShader, viewport 800×450, clareira carregada e câmera em
  repouso. Mediana do tempo de CPU de 3,6 para 3,0 ms. p95 de 4,8 para 3,7 ms.
  A medição representa essa cena em renderização por software.
- O SHA servido pode ser conferido no `build.json` do GitHub Pages.
  [Arquitetura](../../ARCHITECTURE.md) · [Workflow do cliente](../../.github/workflows/pages.yml)
