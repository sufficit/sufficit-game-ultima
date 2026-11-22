# PLAN-PLAYER-ASSETS — Assets GLB CC0 e a evolução dos modelos do mundo

**Status**: ✅ concluído em 22/11/2026 (fases 1–5) ·
[atividade](activities/202611220130-player-assets.md) ·
[fase 1 (GLB CC0)](activities/) registrada no commit `94a2231`

O jogo nasceu 100% paramétrico: modelos são descritores de primitivas em DLLs C#
verificadas por SHA-256 (`Ultima.Models.*`), com texturas procedurais. Este plano
abriu uma segunda via — **assets de malha (GLB) com catálogo assinado** — para
enriquecer o cenário com modelos low-poly CC0 e permitir que **os próprios
jogadores melhorem e montem o mundo**, inclusive gerando/substituindo modelos por
IA (interna ou via API).

Decisão de estilo: **low-poly por enquanto** (Quaternius/KayKit/Kenney, CC0),
permitindo trocas e acréscimos baratos conforme o jogo cresce.

## Fase 1 ✅ — Pipeline GLB CC0 com catálogo assinado (concluída em 22/11/2026)

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
  - `game.glb.status()` para validação e `game.glb.register(id, bytes)` —
    a costura das fases seguintes.
- [x] Import via `SceneLoader.ImportMeshAsync` com URL de Blob + extensão de plugin
      explícita (`'.glb'`): o core decodifica o binário GLB e alimenta o plugin — sem
      arquivo temporário, sem segunda ida à rede depois do fetch verificado.

## Fase 2 ✅ — Evolução paramétrica in-game (concluída em 22/11/2026)

- [x] `ModelLoader.Rebuild(id, seed, detail)` reconstrói o descritor com novos
      `ModelParams` (o modelo continua sendo dados+parâmetros, sem malha nova);
      ponte `ModelsInterop.VaryModel` exposta ao JS via `game.init(canvas, bridge, modelsRef)`.
- [x] `game.models.vary(id, {seed, detail})`: descarta malhas/instâncias antigas
      (`disposeModel`), re-registra e re-espalha dentro do orçamento normal.
- [x] Painel **Oficina do mundo** no HUD (Index.razor): "Árvores novas/densas/jovens",
      "Pedras novas", "Grama exuberante" — o jogador vê o cenário responder na hora.
- [ ] (futuro) Guardiã/Andarilho oferecerem variações conversando (WebLLM escolhe
      seed/detalhe): a costura é a mesma `game.models.vary`.

## Fase 3 ✅ — Geração de modelos via API (concluída em 22/11/2026)

- [x] Backend `app/WorldAssets.cs`: `POST /v1/assets/generate {prompt}` com dois motores —
      **gerador procedural local** (C# escreve o binário GLB na mão: cristal/totem/flor
      low-poly determinísticos por prompt+seed) e **adapter Meshy** (`MESHY_API_KEY`,
      text-to-3D com job + poll `GET /v1/assets/jobs/{id}`).
- [x] Tudo entra no mesmo catálogo assinado (`GET /v1/assets`, bytes em
      `GET /v1/assets/file/{nome}` com cache imutável) — o cliente baixa, confere
      SHA-256 e planta com `game.glb.register` + espalhamento determinístico.
- [x] UI: campo de prompt na oficina ("ex.: cristal encantado azul").
- Nota de licença: Meshy free = **CC-BY 4.0** (exige atribuição; ver `assets/LICENSE.md`
  se usado); planos pagos dão ownership sem crédito. O gerador local não tem restrição.

## Fase 4 ✅ — Upload/edição pelos jogadores (concluída em 22/11/2026)

- [x] `POST /v1/assets/upload` (token de sessão, corpo binário): validação server-side —
      magic do GLB, ≤ 2 MB, ≤ 30 mil triângulos (parse do chunk JSON), id em slug,
      crédito ao autor, limite de 200 assets.
- [x] Espalhamento determinístico por id do asset (`game.glb.scatter` com RNG semeado
      pelo hash): **todos os clientes veem o mundo igual** sem posições no servidor;
      sync do catálogo a cada 30 s (`js/workshop.js`).
- [x] UI: input de arquivo + escala em metros na oficina; recusas do servidor viram
      mensagens claras.
- [ ] (futuro) Catálogo por mundo (hoje global), moderação por whitelist e GC de órfãos.

## Fase 5 ✅ — Substituição opcional dos modelos procedurais (concluída em 22/11/2026)

- [x] Mecanismo de estilo do cenário: `game.scenery` (`procedural` padrão ↔ `lowpoly`),
      persistido em `localStorage`, toggle na oficina com "recarregar para aplicar".
- [x] Alternativas CC0 pinheiro/pedra/grama (`flora.pine`, `rock.boulder2`,
      `scenery.grass2`) substituem os procedurais com as **mesmas contagens e regras**
      (64 árvores/26 pedras/420 tufos); no estilo padrão carregam sem espalhar (reserva).
- [x] Default mantido em `procedural` (identidade visual atual + base da fase 2); a
      troca é uma escolha do jogador, não uma imposição do build.

---

### Validação

- **Fase 1** (commit `94a2231`): local (publish servido, Chrome headless via CDP)
  6/6 assets, 450/450 instâncias, 0 erros, 0 frames >33 ms; GLB com byte corrompido
  recusado por SHA-256 com mundo intacto; produção validada ao vivo no Pages.
- **Fases 2–5** (commits seguintes): backend local (curl) — geração local devolve GLB
  válido (70 tris, base no chão), upload aceito/recusado (tris/magic/sem token);
  navegador (CDP) — `vary` re-espalha árvore (2× timings sem erro), `generate` cria e
  planta "totem de pedra verde", sync traz assets do servidor para o cliente, upload
  pela UI publica "prop.stump" no catálogo e planta, estilo lowpoly ativa 64/26/420
  alternativos com o mesmo visual coeso (sem placeholders/flutuação).
