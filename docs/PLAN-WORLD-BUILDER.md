# PLAN-WORLD-BUILDER — Mundo expansível: edição 3D, geração assistida e persistência durável

**Status**: design aprovado em 22/11/2026 (decisões do usuário abaixo) · fases pendentes
· absorve o escopo do [PLAN-TERRAIN-CHUNKS.md](PLAN-TERRAIN-CHUNKS.md)

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

### F3 — Mundo expansível + cristalização assistida `[pendente]`
- [ ] `GET /v1/world/chunk/{cx},{cz}` — estado consolidado (base + ops do journal) e
      evento `world_edited` no stream existente.
- [ ] Chunk inédito: resposta determinística imediata; primeira visita dispara o
      **agente** (v1: regras procedurais de variação — ruínas/clareiras/rios menores;
      v2 opcional: LLM do próprio jogador propõe, servidor valida) que grava ops no
      journal → todos passam a ver a versão cristalizada.
- [ ] Cliente pede chunks conforme explora (além dos 96×96 m atuais).
- [ ] Validação: dois clientes veem o mesmo mundo; exploração contínua sem trancos.

### F4 ✅ — Edição de superfície: sculpt e paint **concluída em 23/11/2026** (rios = cavar canal + pintar água)
- [x] Pincéis no cliente (levantar/cavar/pintar via clique + raio ajustável) — aplicação local
      otimista + op autoritativa no servidor (rate limit, raio máximo, budget/jogador).
- [x] Água pintável (rios = cavar o canal e pintar água; material real na F7) (base do F7).
- [ ] Validação: edição persiste para todos; undo via replay do journal.

### F5 — Volumes: túneis e masmorras `[pendente]`
- [ ] Ops `carve`/`fill` volumétricas (esferas, corredores, salas) → voxelize o chunk
      afetado → **marching cubes** no worker (superfícies suaves, coerentes com o
      visual) → túneis, porões, masmorras sob o heightfield.
- [ ] Colisão/navegação: heightfield + malha dos volumes.
- [ ] Validação: túnel atravessando um morro, persistido e sincronizado; custo de
      meshing amortizado dentro do orçamento.

### F6 ✅ — Objetos do mundo: construir com assets **concluída em 23/11/2026**
- [x] Ops `place`/`remove` de qualquer asset (builtin CC0 + catálogo do servidor) (CC0/gerado/upload) com
      transform completo; substitui gradualmente os discos de overlay do mapa atual.
- [x] UI de construção na Oficina (plantar/remover com seletor de assets carregados) (grade/snap opcional, preview fantasma).
- [x] Validação: plantio por clique real (pick → comando → evento → instância) e replay no boot

### F7 — Qualidade gráfica `[pendente]`
- [ ] PBR + IBL (ambiente iluminando tudo; toggle para o pipeline StandardMaterial
      atual como fallback — mesmo padrão do estilo procedural↔lowpoly).
- [ ] Sombras CSM (longa distância), SSAO discreto, água real (normais + fresnel +
      reflexo), vento por vertex shader na vegetação, DoF leve.
- [ ] Gate de qualidade: `game.perf.report()` dentro do orçamento em GPU de gama média.

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
