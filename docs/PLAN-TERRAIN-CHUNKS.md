# Terreno em chunks com janela deslizante e costura de seams

## Objetivo e critérios

Substituir o chão único por **chunks de terreno em janela deslizante**: o mundo
vira uma grade de tiles; o jogador carrega o tile principal + vizinhos, com
fila de **1 job por frame**; quando um vizinho chega, os tiles limítrofes são
**recompostos para fechar a emenda** (seam) de altura/cores; tiles fora do
alcance são descartados. Névoa atrelada à distância de desenho esconde a borda.

Critérios de aceite:

- Mundo maior que a tela atual (múltiplos chunks) caminhável sem emendas visíveis
  de altura entre tiles vizinhos.
- Carga/descarga por fila com 1 job por frame — sem travadas ao cruzar fronteiras.
- Purge por distância comprovado (memória/malhas de tiles distantes liberadas).
- Névoa existente ajustada por distância de desenho.

## Terreno e referências

- Fonte: `apps/killer/src/tiles.rs` (janela `NeighbourSet`, fila `Job::Load`/
  `Job::Reseam`, purge por distância, 1 job/frame) e
  `crates/killer-assets/src/scene/terrain.rs` (`Grid::seamed`, `TerrainSeam`,
  normais, fatias de lightmap) do lineage-rust; fog por distância em
  `crates/killer-render/src/fog.rs`.
- No Ultima, o chão e os limites de mundo estão em `src/wwwroot/js/game.js`;
  altura por chunk pode vir como conteúdo das DLLs (mesmo pipeline de modelos).

## Checkpoints

1. [pendente] Grade de chunks estática (sem streaming) com costura de altura
   entre vizinhos — prova da emenda invisível.
2. [pendente] Janela deslizante com fila 1 job/frame e purge por distância.
3. [pendente] Névoa atrelada à distância de desenho; limites de mundo viram
   consequência da janela.
4. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1 com um grid 2×2 de alturas diferentes.

Depende de: [PLAN-ASSET-UPLOAD-BUDGET.md](PLAN-ASSET-UPLOAD-BUDGET.md) para a
carga sem travadas (pode ser prototipado antes e integrado depois).
