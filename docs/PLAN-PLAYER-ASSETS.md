# PLAN-PLAYER-ASSETS — Assets GLB CC0 e a evolução dos modelos do mundo

**Status**: fase 1 concluída em 22/11/2026 · fases 2–5 pendentes

O jogo nasceu 100% paramétrico: modelos são descritores de primitivas em DLLs C#
verificadas por SHA-256 (`Ultima.Models.*`), com texturas procedurais. Este plano
abre uma segunda via — **assets de malha (GLB) com catálogo assinado** — para hoje
enriquecer o cenário com modelos low-poly CC0 e, no futuro, permitir que **os
próprios jogadores melhorem e montem o mundo**, inclusive gerando/substituindo
modelos por IA (interna ou via API).

Decisão de estilo: **low-poly por enquanto** (Quaternius/KayKit/Kenney, CC0),
permitindo trocas e acréscimos baratos conforme o jogo cresce.

## Fase 1 ✅ — Pipeline GLB CC0 com catálogo assinado (concluída)

- [x] Curadoria CC0 (Quaternius, via Poly Pizza) em `assets/glb/` com proveniência
      em `assets/LICENSE.md`: arbusto, flores, cogumelo, samambaia, tronco e toco.
- [x] `scripts/build-assets.sh` gera `src/wwwroot/assets/assets.json`
      (id + arquivo + SHA-256); passo novo no CI do Pages; `src/wwwroot/assets/`
      gitignored (gerado, igual a `models/`).
- [x] Módulo `game.glb` em `js/game.js`:
  - loader glTF oficial **vendorizado e pinado**
    (`js/vendor/babylonjs.loaders-9.36.0.min.js`, SRI + sha256 em `assets/LICENSE.md`),
    injetado sob demanda (primeiro paint intacto);
  - download com verificação SHA-256 (`crypto.subtle`) — bytes divergentes são
    recusados e o mundo segue com os demais assets/modelos;
  - bytes validados cacheados no OPFS (chave endereçada por conteúdo: id+sha —
    sobrevive a builds, ao contrário do cache de texturas que inclui o build);
  - materiais PBR→StandardMaterial (aparência coerente com o resto do jogo);
  - protótipo invisível normalizado: **origem no chão** (mesma convenção dos
    descritores DLL) e maior dimensão = `scaleM` metros; instancing por
    `createInstance` no espalhamento;
  - espalhamento na fila de modelos (orçamento 4 ms/frame, lotes de 24) reusando
    `makeInstancePlacer`/`batchedSteps`;
  - `game.glb.status()` para validação e `game.glb.register(id, bytes, opts)` —
    a costura das fases seguintes.
- [x] Import via `SceneLoader.ImportMeshAsync` com URL de Blob + extensão de plugin
      explícita (`'.glb'`): o core decodifica o binário GLB e alimenta o plugin — sem
      arquivo temporário, sem segunda ida à rede depois do fetch verificado.

## Fase 2 — Evolução paramétrica in-game (IA interna) `[pendente]`

`ModelParams` já carrega `Seed` e `Detail` no contrato das DLLs; a IA local
(WebLLM, `game.ai.*`) pode escolher parâmetros e **variar os modelos existentes
sem gerar malha**: árvores mais retas/tortas, pedras mais angulosas, grama mais
densa. Barato, offline, sem novo formato. Checkpoint: Guardiã oferece opções de
variação e o jogador vê o cenário responder.

## Fase 3 — Geração de modelos via API `[pendente]`

Servidor gera GLB (Meshy/Tripo/…) e publica no **mesmo catálogo** (`assets.json`
gerado também no servidor, ou rota dedicada) com SHA-256; clientes baixam e
verificam como hoje. Notas de licença:

- Meshy free = **CC-BY 4.0** (exige atribuição; ver `assets/LICENSE.md` se usado);
  planos pagos dão ownership sem crédito.
- Alternativa open: treinar/hospedar gerador próprio (ex.: TripoSR self-hosted) —
  sem restrição de licença de saída.

Checkpoint: enviar prompt no jogo → GLB aparece no mundo do jogador.

## Fase 4 — Upload/edição pelos jogadores `[pendente]`

Jogadores montam o mundo (o backend já persiste edições de mapa em `map.json` com
revision/jobs). Extensão natural: catálogo de assets por mundo com whitelist de
hashes (moderação leve), uploads GLB ≤ N MB validados server-side (limite de
triângulos, sem scripts — GLB é dado), e o OPFS da fase 1 vira cache dos assets
baixados. Sinergia direta com o cache OPFS já entregue
([atividade](activities/202611212050-cache-opfs-preparados.md)).

Checkpoint: dois jogadores veem o mesmo asset publicado num mundo compartilhado.

## Fase 5 — Substituição dos modelos procedurais (opcional) `[pendente]`

Trocar `tree.pine`/`rock.boulder`/`scenery.grass` por versões Quaternius é uma
linha no `glbPlacements` + remoção do placement DLL — os dois pipelines convivem.
Fazer só se o estilo visual pedir; os modelos paramétricos seguem como fallback
garantido (placeholders C#) e base da fase 2.

---

### Validação da fase 1

- Local (:8099, publish real): 6/6 assets carregados, 450 novas instâncias,
  0 erros de console; GLB adulterado (byte flipado) recusado por SHA-256 com o
  mundo intacto; `game.perf.report()` sem regressão (orçamento 4 ms respeitado).
- Produção (Pages): commit na `main` → deploy → validação ao vivo no
  `https://sufficit.github.io/sufficit-game-ultima/` (regra do AGENTS.md).
