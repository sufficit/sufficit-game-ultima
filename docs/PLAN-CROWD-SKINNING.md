# Multidões: skeletons compartilhados, bone texture e LOD por tamanho em tela

## Objetivo e critérios

Renderizar **muitos personagens animados** (alvo de referência: 300) com custo
estável: todas as instâncias do mesmo tipo compartilham geometria e skeleton;
as matrizes de ossos de todas as instâncias cabem numa única textura de ossos
(bone texture) com deslocamento por instância; LOD esquelético escolhe a
malha pela altura do personagem em tela; culling por distância + frustum.

Critérios de aceite:

- X personagens idênticos animados simultâneos (X medido no hardware alvo,
  partindo de 100) com FPS estável no site publicado.
- Uma única cópia de geometria/skeleton por tipo de personagem na memória.
- LOD por tamanho em tela: personagens distantes trocam de malha sem "popping"
  perceptível; perto do limite de FPS o degrau reduz.
- Culling: personagens fora do frustum/alcance não custam animação nem draw.

## Terreno e referências

- Fonte: lineage-rust faz skinning 100% em GPU com *palette buffer* global e
  `palette_offset` por instância, 1 write_buffer/frame e LOD por tamanho em
  tela com degrau `keep_min_frame_rate` —
  `crates/killer-render/src/skinned.wgsl` (`bone_matrix()`),
  `crates/killer-render/src/skinned.rs` (`CharacterInstance`, teste de 100
  instâncias), `apps/killer/src/characters.rs` (cache/culling, demo de 300) e
  `crates/killer-assets/src/skeletal_lod.rs`.
- Equivalentes Babylon: `Skeleton` + bone texture (`skeleton.prepare()`/
  `useTextureToOptimizeBoneMatrices`), instâncias de malha, e
  `scene.getActiveMeshes` para culling. O valor da referência está no desenho
  (offset global + 1 upload/frame), não na API.

## Checkpoints

1. [pendente] Um tipo de personagem com N clones animados via bone texture
   compartilhada; medir FPS com N crescendo.
2. [pendente] Culling por distância + frustum; LOD por tamanho em tela.
3. [pendente] Escala para segundo tipo de personagem e cena mista; medir no
   site publicado.
4. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1 depois que existir um personagem com esqueleto animado (hoje o
elenco é esfera + Guardiã); pode ser prototipado com um asset de teste.

Depende de: animação esquelética no pipeline de modelos das DLLs.
