# world-store-v1 — Persistência do mundo: arquivo-first com interface isoladora

**Data**: 22/11/2026 · **Contexto**: [Atividade de persistência do mundo](../docs/activities/202611232303-persistencia-do-mundo.md)
(fase F1 do [Atividade de mundo expansível](../docs/activities/202611232303-mundo-expansivel.md))

**Pergunta do usuário**: "volume durável ou MySQL?" — decisão pelo formato de acesso,
não por gosto.

## Decisões

- **Arquivo-first para o mundo**: journal append-only (`world/world.journal.jsonl`, uma
  op por linha ~60–200 B) + snapshot compactado (`world/world.snapshot.json`), ambos num
  **volume durável** (PVC `world-data`, `ULTIMA_DATA_DIR=/data`). O padrão de acesso do
  mundo é append minúsculo + replay por chunk + blobs (GLB) — arquivo ganha em
  desempenho (disco local µs vs hop de rede), latência no caminho do jogo, blobs nativos
  e zero dependência extra no pod. Atomicidade via `.tmp` + rename; histórico/undo por
  replay quase de graça.
- **Interface isoladora `IWorldStore`** (`Load`/`Append`/`Compact`): o `GameWorld` nunca
  sabe o formato — trocar implementação é código novo, não redesign.
- **Escada de migração com gatilhos mensuráveis**:
  1. JSONL + snapshots (agora) — até ~100k ops;
  2. **SQLite** no mesmo volume — quando ops > ~100k ou acesso aleatório por chunk
     pesado; arquivo único, sem rede;
  3. **MySQL/MariaDB** — quando surgirem requisitos *relacionais* (contas persistentes,
     economia transacional, moderação com queries ricas, múltiplas réplicas do backend).
     Mundo (geometria/assets) permanece em arquivo mesmo nesse cenário híbrido.
- **Jobs no journal**: ops `job`/`jobDone`/`jobCancel` persistem o bookkeeping (progresso
  por tempo de parede sobrevive a restarts, como o map.json fazia) com `doneAt` gravado.

## Alternativas descartadas

- **MySQL desde já**: escrever cada edição via INSERT acrescenta rede+pool no caminho
  autoritativo, blobs de GLB em tabela são anti-pattern, e acopla o lifecycle do pod do
  jogo a um DB externo — sem nenhum requisito relacional atual que justifique.
- **Snapshot único (map.json) para sempre**: reescrita integral por edição, sem
  histórico/undo — não escala para grandes edições (motivo da mudança).
