# Documentação do Ultima

- [PROJECT.md](../PROJECT.md) — visão, premissas e marcos do jogo.
- [decisions/](../decisions/) — registros de decisão históricos (input, visual, API-first).

## Planos ativos (`PLAN-*.md`)

Cada frente de trabalho tem um plano próprio. Um plano permanece aberto
enquanto houver pendências; ao concluir o escopo inteiro ele é convertido em
[atividade](activities/) (veja o [AGENTS.md](../AGENTS.md)).

Planos históricos:

1. ✅ [PLAN-WORKER-OFFSCREEN-CANVAS.md](PLAN-WORKER-OFFSCREEN-CANVAS.md) —
   **concluído em 22/11/2026**: motor em `game-core.js` sem DOM, shell com worker
   OffscreenCanvas + fallback `?render=main`, loader glTF verificado por SHA-384 no
   worker. Atividade:
   [202611220130-motor-worker-offscreen.md](activities/202611220130-motor-worker-offscreen.md).
2. ✅ [PLAN-DLL-OPFS-CACHE.md](PLAN-DLL-OPFS-CACHE.md) — **concluído em 21/11/2026**:
   OPFS com chave SHA-256 de build+escopo; texturas e terreno prontos nas revisitas
   (init 106→41 ms em produção). Atividade:
   [202611212050-cache-opfs-preparados.md](activities/202611212050-cache-opfs-preparados.md).
3. ✅ [PLAN-NPC-AI-DECISIONS.md](PLAN-NPC-AI-DECISIONS.md) — **concluído em
   22/11/2026**: cérebros por NPC (Guardiã e Andarilho), agendador com budget por
   tick, WebLLM/API como produtores externos; 34 testes. Atividade:
   [202611220040-ia-npcs-decisoes.md](activities/202611220040-ia-npcs-decisoes.md).
4. ✅ [PLAN-ASSET-UPLOAD-BUDGET.md](PLAN-ASSET-UPLOAD-BUDGET.md) — **concluído em
   21/11/2026**: fila com orçamento de 4 ms/frame, scatter em lotes e
   `game.perf.report()`. Atividade:
   [202611212030-upload-amortizado-modelos.md](activities/202611212030-upload-amortizado-modelos.md).

Futuros (bom ter na manga):

11. [PLAN-MOB-BRAIN.md](PLAN-MOB-BRAIN.md) — IA determinística de criaturas
    (MobBrain): percepção, perseguição, ataque e retorno decididos sobre
    snapshot; proposta inicial, sem implementação.

Fonte técnica das frentes 1–11: análise do repositório
[w3nder/lineage-rust](https://github.com/w3nder/lineage-rust)
(cliente Lineage II C4 em wgpu, web WASM + WebGPU; clone local de referência em
`/mnt/sufficit/lineage-rust`). Cada plano cita os arquivos-fonte correspondentes.

## Atividades (`activities/`)

- [Publicação do backend no Sufficit Run](activities/202611232303-publicacao-do-backend-no-run.md)

- [Mundo expansível](activities/202611232303-mundo-expansivel.md)

- [Persistência do mundo](activities/202611232303-persistencia-do-mundo.md)

- [Catálogo de modelos do mundo](activities/202611232303-catalogo-de-modelos-do-mundo.md)

- [Oficina, corredores e perfil Cinemático](activities/202611232159-oficina-volumes-e-graficos.md)

- [Multidões esqueléticas](activities/202611232144-multidoes-esqueleticas.md)

- [Marcas da animação](activities/202611232144-marcas-da-animacao.md)

- [Diálogo e Diário em JSON](activities/202611232127-janelas-declarativas.md)

- [Câmera e streaming do terreno](activities/202611232122-camera-e-streaming.md)

- [Terreno em C# WebAssembly](activities/202611231906-terreno-csharp-wasm.md)

- [Curvas nas bordas das escavações](activities/202611231851-relevo-escavado-sem-v.md)

- [Emendas naturais no terreno](activities/202611231831-emendas-do-terreno.md)

- [Migração de serviços JavaScript para C#](activities/202611231825-migracao-js-para-csharp.md)

- [Movimento e expressão dos personagens](activities/202611231810-expressao-dos-personagens.md)

- [Destaque do alvo de coleta](activities/202611231755-mira-de-coleta.md)

- [Precisão dos cliques e escavação em camadas](activities/202611231736-precisao-e-camadas-da-coleta.md)

- [Apoio dos pés em troncos baixos](activities/202611231730-apoio-nos-troncos.md)

- [Mochila e coleta de recursos](activities/202611231722-mochila-e-coleta.md)

- [Passo articulado sobre obstáculos baixos](activities/202611231701-passo-articulado-sobre-obstaculos.md)

- [Colisão do jogador e inspeção de objetos](activities/202611231650-colisao-e-interacao-com-objetos.md)

- [Texturas de grama e terra, luz, vento e chuva](activities/202611231639-grama-terra-e-clima.md)

- [Oficina do mundo dentro do Painel do jogo](activities/202611231606-oficina-no-painel.md)

- [Estilos da Sufficit.Blazor.UI nos menus](activities/202611231448-estilos-sufficit-blazor-ui-nos-menus.md)

- [Correção do poço e acabamento do terreno](activities/202611231402-acabamento-do-terreno.md)

- [Correção da renderização do terreno](activities/202611231348-renderizacao-do-terreno.md)

- [Conflitos de movimento no harness mobile](activities/202611231306-conflitos-do-harness-mobile.md)

Documentos de atividade (`AAAAMMDDHHMM-slug.md`) registram frentes concluídas:
investigação, alterações e validação com evidências. São gravadas quando o
escopo inteiro de um plano é concluído.
