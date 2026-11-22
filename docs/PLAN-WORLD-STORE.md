# PLAN-WORLD-STORE — Persistência durável do mundo (F1 do WORLD-BUILDER)

**Status**: ✅ concluído em 22/11/2026 · **Decisão**: [decisions/world-store-v1.md](../decisions/world-store-v1.md)
(arquivo-first + `IWorldStore`; MySQL só para dados relacionais futuros)

## Objetivo

Os dados do mundo (edições de mapa, jobs, assets da oficina) saem do `/tmp` efêmero do
pod e ganham **volume durável + journal append-only**: sobrevivem à recriação do pod,
ganham histórico/undo por replay e preparam o terreno para as fases F2+ (chunks,
cristalização IA, sculpt/carve).

## Entregas e checkpoints

- [x] **C1 — Store no backend**: `app/WorldStore.cs` — `IWorldStore`
      (`Load`/`Append`/`Compact`) + `JournalWorldStore`: journal
      `world/world.journal.jsonl` (ops `tile`/`job`/`jobDone`/`jobCancel`, uma por
      linha, `op` seq crescente) + snapshot `world/world.snapshot.json` (materialização
      com `opSeq` consumido). Replay: snapshot + ops com `op` > `opSeq`. Compact
      automático na carga quando o journal passa de 2.000 linhas.
- [x] **C2 — Migração do map.json legado**: na primeira carga sem snapshot, o
      `map.json` existente vira snapshot inicial (`map.json.migrated` preservado).
- [x] **C3 — GameWorld no store**: os 5 sítios de mutação (map/edits, dig/start_job,
      stop_job, job_done do tick, job de ordem automática) passam a appendar ops;
      `MapSave` deixa de existir.
- [x] **C4 — Volume durável em produção**: PVC `world-data` (1 Gi, `eveo-zfs-rwx`) +
      patch do deployment `workspace-app` (mount `/data` + `ULTIMA_DATA_DIR=/data`).
      Validar contra o reconciler da plataforma (patch pode ser revertido — se ocorrer,
      virar feature da plataforma).
- [x] **C5 — Prova de durabilidade**: editar mapa + gerar asset em produção →
      **deletar o pod** → novo pod serve a mesma edição/revisão e o catálogo; journal
      com as ops no volume.

## Validação (todas executadas)

- [x] Local: edição+dig gravam ops; restart preserva revisão/tiles e retoma job (jobDone
      gravado); seqs monotônicas pós-restart (bug de reset corrigido no caminho);
      map.json legado migrado com job ativo retomado.
- [x] Produção: PVC world-data (eveo-zfs-rwx) montado em /data com ULTIMA_DATA_DIR;
      edição → ops no journal; asset → catálogo no volume; **pod deleto → novo pod
      restaura rev 2, tiles water/rock e catálogo de assets pelo replay do journal**.

- [ ] Editar mapa → arquivo `world.journal.jsonl` com as ops `tile`; reiniciar o
      servidor no mesmo dataDir → `/v1/game/map` devolve a mesma revisão/tiles.
- [ ] `map.json` legado com tile+job → boot → snapshot criado, backup `.migrated`,
      job retomado (progresso por tempo de parede).
- [ ] Replay == estado em memória (revisão, tiles, jobs) após N edições mistas.

## Fora de escopo (fases seguintes do WORLD-BUILDER)

Formato por chunk, ops de sculpt/carve, cristalização IA, SQLite/MySQL (gatilhos na
decisão world-store-v1).
