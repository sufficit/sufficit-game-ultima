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
2. [PLAN-DLL-OPFS-CACHE.md](PLAN-DLL-OPFS-CACHE.md) —
   cache OPFS de assets preparados com chave SHA-256, estendendo o pipeline de DLLs.
3. [PLAN-NPC-AI-DECISIONS.md](PLAN-NPC-AI-DECISIONS.md) —
   IA de NPCs por decisões sobre snapshot com budget por tick; WebLLM como produtor orquestrado.
4. [PLAN-ASSET-UPLOAD-BUDGET.md](PLAN-ASSET-UPLOAD-BUDGET.md) —
   upload amortizado de modelos (budget de ms por frame, limite de requisições em voo).

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
