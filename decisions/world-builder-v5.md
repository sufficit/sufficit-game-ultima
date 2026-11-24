# world-builder-v5 — Mundo expansível com edição 3D e cristalização assistida por IA

**Data**: 22/11/2026 · **Contexto**: [Atividade de mundo expansível](../docs/activities/202611232303-mundo-expansivel.md)

**Pedido do usuário**: pensar desde já em grandes edições (com boa qualidade gráfica e
persistência); necessidade explícita de **túneis, masmorras, rios**; o mundo deve
**se auto-incrementar conforme os jogadores andam**, com o **agente de IA gerando
aleatoriedade local** e sincronização quando a área ainda não existe.

## Decisões

- **Paradigma de edição: heightfield esculpível + volumes de escavação (não voxel
  puro).** A superfície continua o terreno procedural atual, editável por pinceladas
  (Δaltura/material/água); túneis e masmorras são ops `carve`/`fill` volumétricas
  voxelizadas por chunk e renderizadas com marching cubes (suaves, coerentes com a
  identidade visual). Motivo: atender túneis/masmorras sem trocar a estética por
  blocos nem multiplicar cliente/rede/servidor; ops ficam compactas (~60 B) no journal.
- **Mundo infinito determinístico com cristalização na primeira visita.** Chunks
  gerados por `(seed, cx, cz)` — idênticos em todos os clientes sem custo de rede; ao
  serem visitados pela primeira vez, um agente pode enriquecer o chunk (variação de
  bioma, ruínas, rios menores) gravando ops no journal, que passa a ser autoritativo
  para todos. É o modelo "gerador + delta persistido" do Minecraft com a IA no papel
  de variar o gerador — e a IA nunca fica no caminho crítico: o chunk determinístico
  responde na hora, a cristalização chega por evento.
- **Persistência: journal append-only primeiro.** `edits.jsonl` + snapshots
  compactados dão histórico/undo quase de graça e atomicidade trivial; SQLite entra
  quando o volume de ops justificar (~100k); formato binário estilo Anvil só se
  volumes meshados ultrapassarem isso.
- **Durabilidade imediata**: dados do mundo em volume dedicado (PVC + `ULTIMA_DATA_DIR`),
  saindo do `/tmp` efêmero do pod.

## Alternativas descartadas

- **Voxel puro (estilo Minecraft)**: atenderia túneis de forma mais direta, mas troca a
  identidade visual por blocos, encarece meshing/rede e conflita com a meta de
  "boa qualidade gráfica".
- **Heightfield puro (sem volumes)**: mantém visual e simplicidade, mas não permite
  túneis/masmorras — requisito explícito.
- **Snapshot JSON apenas (status quo)**: suficiente para o mapa de tiles atual, mas sem
  histórico, sem undo e com reescrita integral por edição — não escala para grandes
  edições.
