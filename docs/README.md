# Documentação do Ultima

- [PROJECT.md](../PROJECT.md) — visão, premissas e marcos do jogo.
- [decisions/](../decisions/) — registros de decisão históricos (input, visual, API-first).

## Planos ativos (`PLAN-*.md`)

Cada frente de trabalho tem um plano próprio. Um plano permanece aberto
enquanto houver pendências; ao concluir o escopo inteiro ele é convertido em
[atividade](activities/) (veja o [AGENTS.md](../AGENTS.md)).

Prioritários:

1. [PLAN-WORKER-OFFSCREEN-CANVAS.md](PLAN-WORKER-OFFSCREEN-CANVAS.md) —
   mover o loop Babylon.js para um Web Worker com OffscreenCanvas.
2. ✅ [PLAN-DLL-OPFS-CACHE.md](PLAN-DLL-OPFS-CACHE.md) — **concluído em 21/11/2026**:
   OPFS com chave SHA-256 de build+escopo; texturas e terreno prontos nas revisitas
   (init 106→41 ms em produção). Atividade:
   [202611212050-cache-opfs-preparados.md](activities/202611212050-cache-opfs-preparados.md).
3. [PLAN-NPC-AI-DECISIONS.md](PLAN-NPC-AI-DECISIONS.md) — **em andamento** (3/4
   checkpoints): snapshot/decisões/agendador com budget entregues e validados em
   produção; falta o segundo NPC para escalar o desenho.
4. ✅ [PLAN-ASSET-UPLOAD-BUDGET.md](PLAN-ASSET-UPLOAD-BUDGET.md) — **concluído em
   21/11/2026**: fila com orçamento de 4 ms/frame, scatter em lotes e
   `game.perf.report()`. Atividade:
   [202611212030-upload-amortizado-modelos.md](activities/202611212030-upload-amortizado-modelos.md).

Futuros (bom ter na manga):

5. [PLAN-CAMERA-TRACE-COLLISION.md](PLAN-CAMERA-TRACE-COLLISION.md) —
   câmera orbital com colisão por trace em degraus.
6. [PLAN-TERRAIN-CHUNKS.md](PLAN-TERRAIN-CHUNKS.md) —
   terreno em chunks com janela deslizante e costura de seams.
7. [PLAN-CROWD-SKINNING.md](PLAN-CROWD-SKINNING.md) —
   multidões: skeletons compartilhados, bone texture e LOD por tamanho em tela.
8. [PLAN-UI-DECLARATIVE.md](PLAN-UI-DECLARATIVE.md) —
   janelas do jogo declaradas em JSON, renderizadas por componentes Razor.
9. [PLAN-ANIMATION-NOTIFIES.md](PLAN-ANIMATION-NOTIFIES.md) —
   eventos de gameplay amarrados a pontos da timeline das animações.

Fonte técnica das frentes 1–9: análise do repositório
[w3nder/lineage-rust](https://github.com/w3nder/lineage-rust)
(cliente Lineage II C4 em wgpu, web WASM + WebGPU; clone local de referência em
`/mnt/sufficit/lineage-rust`). Cada plano cita os arquivos-fonte correspondentes.

## Atividades (`activities/`)

Documentos de atividade (`AAAAMMDDHHMM-slug.md`) registram frentes concluídas:
investigação, alterações e validação com evidências. São gravadas quando o
escopo inteiro de um plano é concluído.
