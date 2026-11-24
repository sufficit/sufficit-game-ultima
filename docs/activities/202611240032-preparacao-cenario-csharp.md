# Preparação de cenário e multidão em C# WebAssembly

## Investigação

O espalhamento repetia o mesmo fluxo em três loops JavaScript. Cada candidato
consultava altura e inclinação pelo WASM e cada instância aceita consultava
a altura editada. A preparação ainda rasterizava casca, folhagem e mapas de
normais no JavaScript. A multidão fazia os testes de frustum e LOD por pessoa
em JavaScript.

## Alterações

- `TerrainScatter` conserva as sementes e a ordem do Mulberry32 e devolve até
  24 transformações por chamada. O Babylon.js aplica as transformações e os
  registros de coleta. A altura natural já calculada é reaproveitada na
  projeção da altura editada.
- A rasterização de casca, folhagem, água e relevo e a preparação de deformações
  e oclusão por vértice retornam blocos binários. A fila separa textura, relevo
  e material. O adaptador reaproveita os pixels do mapa de relevo por textura,
  resolução e intensidade.
- `TerrainCrowd` calcula grade inicial, distância, frustum e LOD com histerese.
  O Babylon.js fornece os planos da câmera, copia as instâncias visíveis
  e roda a animação esquelética na GPU.

## Validação e entrega

- Build Release dos dois clientes WASM e 110 verificações em `RulesSmoke`.
- Em Chromium com SwiftShader, 1.024 instâncias do cenário e 20 da Oficina
  preservaram nomes, posições, escalas e rotações a oito casas decimais.
  Quatro texturas também produziram os mesmos hashes no navegador.
- Multidões de 100 e 300 personagens passaram em worker e main.
  No build final, também passaram janelas declarativas, variação da Oficina
  pelo diálogo e cache OPFS. A colisão e o movimento foram conferidos nos
  dois modos antes do ajuste final de rasterização e fila de materiais.
- Comparação em Chromium com SwiftShader, viewport 800×450, worker e nove
  tarefas de cenário por build, incluindo criação e upload do Babylon.js.

| Custo de CPU | Antes | Depois |
| --- | ---: | ---: |
| Mediana por tarefa | 1,9 ms | 1,6 ms |
| p95 por tarefa | 13,3 ms | 14,6 ms |
| Maior passo de uma tarefa | 7,6 ms | 7,1 ms |
| Preparação da grama | 13,3 ms | 7,0 ms |

A medição cobre a preparação nesse cenário e usa renderização por software.
[Arquitetura e fronteira com JavaScript](../../ARCHITECTURE.md)
