# Câmera orbital com colisão por trace em degraus

## Objetivo e critérios

Câmera terceira pessoa atrás do personagem com zoom/drag que **não atravessa o
terreno nem o cenário**: quando o raio personagem→câmera está bloqueado, o zoom
recua em degraus (trace de volta em passos) até achar posição livre, em vez de
pular para dentro da geometria.

Critérios de aceite:

- Câmera orbital com follow suave, drag para orbitar e wheel para zoom, no lugar
  do acompanhamento atual.
- Colisão por trace em degraus (`hit_loop`): comprovada em cenário denso
  (árvores/pedras) — a câmera nunca fica dentro de malha nem "teleporta".
- O traçado de colisão é um callback injetado (`trace(from, to)`) fornecido pela
  camada de mundo; a câmera não conhece a física.
- Funciona com WASD + mouse; sem regressão nos limites do mundo.

## Terreno e referências

- Fonte: `apps/killer/src/camera.rs` do lineage-rust — `OrbitCamera` com
  `hit_check` (um trace alvo→olho), `hit_loop` (traces de volta em degraus) e
  "volume camera" (rotações intermediárias quando o raio está bloqueado).
- Equivalentes Babylon: `ArcRotateCamera` + `camera.checkCollisions`/
  `collision` com raio; o valor está no algoritmo de degraus e na inversão de
  dependência do `trace`.

## Checkpoints

1. [pendente] Trocar a câmera atual por `ArcRotateCamera` com follow, drag e zoom.
2. [pendente] Implementar `trace(from,to)` na camada de mundo (pick simples
   contra chão + malhas do cenário) e o recuo em degraus.
3. [pendente] Validar em cenário denso no site publicado; ajustar passos e limites.
4. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1, junto com um cenário de teste denso (aglomerar árvores/pedras
para provocar oclusão da câmera).
