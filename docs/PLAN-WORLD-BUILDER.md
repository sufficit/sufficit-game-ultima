# PLAN-WORLD-BUILDER — Mundo expansível: edição 3D, geração assistida e persistência durável

**Status**: geração 1 concluída em 23/11/2026 — F1, F2, F3, F4, F5a, F6 e F7a entregues
e validadas em produção; F5b (túneis cobertos) e F7b (gráficos avançados) seguem como
frentes próprias com bloqueio documentado (colisão 3D / QA em GPU real) ·
absorve o escopo do [PLAN-TERRAIN-CHUNKS.md](PLAN-TERRAIN-CHUNKS.md)

## Decisões que moldam este plano (decisão do usuário, 22/11/2026)

1. **Edição precisa de túneis, masmorras, rios** → paradigma = **heightfield esculpível
   + volumes de escavação** (ops carve/fill renderizadas com marching cubes no worker),
   não voxel puro: mantém a identidade visual e a qualidade gráfica, com ops compactas
   no journal. Registro formal: [decisions/world-builder-v5.md](../decisions/world-builder-v5.md).
2. **O mundo se auto-incrementa conforme os jogadores andam** → chunks sob demanda,
   gerados deterministicamente (seed + coordenadas) e **cristalizados** na primeira
   visita: o agente de IA pode enriquecer (variação de bioma, ruínas, clareiras) uma
   única vez, no servidor; daí em diante é autoritativo para todos — "sincroniza caso
   ainda não exista" no modelo seed+delta do Minecraft, com a IA variando o gerador.
3. **Persistência durável já**: os dados do mundo saem do `/tmp` efêmero e ganham
   volume dedicado + journal append-only (histórico/undo quase de graça).

## Modelo do mundo (3 camadas)

| Camada | Quem gera | Persistência | Tamanho típico |
|---|---|---|---|
| **Base** — heightfield, biomas, rios | gerador determinístico (ruído atual, por chunk) | nenhuma (recomputável) | 0 bytes |
| **Cristalização IA** — variação da 1ª visita | agente (regras agora; LLM opcional depois) | journal (ops) | ~KB/chunk |
| **Edições** — sculpt/paint/carve/place | jogadores | journal (ops) | ~60 B/op |

Journal único append-only (JSONL) + snapshots periódicos (compaction); revisão global
transmitida pelos eventos existentes (`world_edited`). SQLite quando passar de ~100k ops.

## Fases

### F1 ✅ — Persistência durável **concluída em 22/11/2026** → [PLAN-WORLD-STORE.md](PLAN-WORLD-STORE.md)
- [x] PVC dedicado no deployment do backend (volume `data` + `ULTIMA_DATA_DIR=/data`)
      via patch do deployment — mapa e `assets/` saem do `/tmp`.
- [x] Mapa migra de snapshot `map.json` para **journal JSONL** (ops append-only) +
      snapshot compactado, atrás da interface `IWorldStore`
      ([decisão world-store-v1](../decisions/world-store-v1.md): arquivo-first,
      SQLite ~100k ops, MySQL só para dados relacionais).
- [x] Validação: recriar o pod → mundo e assets intactos; replay do journal ==
      snapshot.

### F2 ✅ — Terreno em chunks no cliente **concluída em 23/11/2026** (absorve TERRAIN-CHUNKS)
- [x] Chunks de 32×32 m (mesh por chunk no worker), janela deslizante pela câmera,
      costura de seams; rebuilds na fila de orçamento existente (4 ms/frame).
- [x] Gerador determinístico por coordenadas de mundo (base da F3) — base para o mundo infinito.
- [x] Validação: 16 chunks na fila orçada (pior passo 19 ms, 0 frames >33 ms), costura sem emenda, replay de edições no boot (mesh = dados: -6,13 m) sem frames >33 ms; seams invisíveis.

### F3 ✅ — Mundo expansível + cristalização assistida **concluída em 23/11/2026** (regras v1)
- [x] Mundo cresce ao andar: andar até ±400 m com streaming de chunks (janela 5×5,
      dispose de distantes, aquecimento OPFS por aproximação); montanhas do gerador
      são a barreira natural.
- [x] Cristalização v1 (regras determinísticas por hash do chunk): clareira, lagoa
      ou marco na primeira visita, gravado no journal — autoritativo para todos;
      chunks visitados reconstruídos no boot. LLM como produtor assíncrono: futura.
- [x] Validação: produção — jogadores andando cristalizam chunks (marcos `chunk x,z`
      no catálogo) e a janela desliza (25→30 chunks ao andar, 0 erros).
- [x] Mesmo mundo para todos via journal/eventos (F1/F4 já provados).

### F4 ✅ — Edição de superfície: sculpt e paint **concluída em 23/11/2026** (rios = cavar canal + pintar água)
- [x] Pincéis no cliente (levantar/cavar/pintar via clique + raio ajustável) — aplicação local
      otimista + op autoritativa no servidor (rate limit, raio máximo, budget/jogador).
- [x] Água pintável (rios = cavar o canal e pintar água; material real na F7) (base do F7).
- [ ] Validação: edição persiste para todos; undo via replay do journal.

### F5a ✅ — Passagens abertas **concluída em 23/11/2026**
- [x] Action `passage` (dois cliques na Oficina): o servidor expande a linha em ops de
      escultura espaçadas + paredes de rocha pintadas, atômico (≤60 m). Validado:
      corredor a -1,5 m vs +4,4 m ao lado (atravessa colinas), 11 ops em produção.

### F5b — Túneis cobertos e masmorras `[pendente]` — decisão de arquitetura
- **Bloqueio real**: o jogador anda sobre `terrainHeight` (colisão de heightfield) —
  túneis COBERTOS exigem evoluir o modelo de colisão para volumes 3D + marching cubes
  no worker. É uma frente própria (colisão, física da câmera, sincronização), não um
  incremento da F5a. Passagens abertas (F5a) cobrem o jogo atual.
- [ ] Quando abrir: op `carve` volumétrica → voxelize o chunk → marching cubes;
      colisão por malha de volume; iluminação própria das grutas.

### F6 ✅ — Objetos do mundo: construir com assets **concluída em 23/11/2026**
- [x] Ops `place`/`remove` de qualquer asset (builtin CC0 + catálogo do servidor) (CC0/gerado/upload) com
      transform completo; substitui gradualmente os discos de overlay do mapa atual.
- [x] UI de construção na Oficina (plantar/remover com seletor de assets carregados) (grade/snap opcional, preview fantasma).
- [x] Validação: plantio por clique real (pick → comando → evento → instância) e replay no boot

### F7a ✅ — Água, luz e feedback **concluída em 23/11/2026**
- [x] Pintura de água vira disco translúcido que acompanha o terreno (lagoas da
      cristalização incluídas); sombras/ambiente clareados (crateras e passagens
      legíveis); cursor de mira no modo construção.

### F7b — Qualidade gráfica avançada `[pendente]`
- **Bloqueio prático**: PBR+IBL/CSM/SSAO precisam de QA visual em GPU real — o
  contêiner de validação roda SwiftShader (software), onde qualquer ajuste fino de
  imagem não é confiável. Fica para uma sessão com GPU.
- [ ] PBR + IBL (toggle com fallback StandardMaterial), sombras CSM, SSAO discreto,
      água com normais/fresnel, vento por vertex shader na vegetação, DoF leve.
- [ ] Gate: `game.perf.report()` dentro do orçamento em GPU de gama média.

## Riscos e mitigações

- **Marching cubes no worker** (custo de meshing): chunks 32 m + rebuild amortizado na
  fila; volumes só onde houver carve.
- **IA na geração**: latência/custo — chunk nasce determinístico e evolui
  assincronamente (padrão produtor-assíncrono já consagrado nos NPCs); LLM nunca no
  caminho crítico do jogar.
- **PVC na plataforma run**: deployment é platform-managed e reconcilers podem reverter
  patches manuais — validar pós-patch; se reverter, virar feature da plataforma.
- **Journal cresce**: compaction periódica + SQLite no limite (~100k ops).

## Fontes técnicas

- Formato de chunks/region do Minecraft (Anvil): tabela de offsets + compressão por
  chunk — referência para o F5 se os volumes crescerem além de ops.
- lineage-rust (`/mnt/sufficit/lineage-rust`): amortização de entrada e streaming.
- Infra de agentes de NPC do próprio jogo (`NpcScheduler`/WebLLM): molde do agente do F3.
