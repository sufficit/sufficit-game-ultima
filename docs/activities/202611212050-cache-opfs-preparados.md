# Cache OPFS de assets preparados com chave SHA-256 (texturas e terreno)

## Investigação

Depois da fila amortizada
([202611212030-upload-amortizado-modelos.md](202611212030-upload-amortizado-modelos.md)),
o custo restante de CPU na entrada do mundo era a **geração procedural** que
repetia a cada visita: 5 texturas (bark/foliage/rock/grass/detail, loops de
pixels 128–256 px, duas com heightmap para o normal map) e a malha do terreno
(37.249 vértices com ruído por vértice para posição e cor, mais normais). A
referência veio do lineage-rust: um worker de preparação grava a cena pronta em
OPFS com chave de hash de build+opções e publicação atômica
(`web/prepare-worker.js`, `web/preparation-cache.mjs`).

## Alterações

- Novo `src/wwwroot/js/assets.js` (commit `bff547f`): diretório OPFS
  `ultima-assets`; chave = SHA-256 de `{build do jogo, escopo, parâmetros}`
  (build lido do `<meta name="ultima-build">`, o mesmo stamp do workflow);
  gravação binária `[magic][len JSON][metadata][partes]` via `.tmp` + renome
  (`FileSystemFileHandle.move` quando suportado, fallback com sobrescrita);
  leitura valida magic, tamanhos das partes e comprimento total — corrupção
  vira cache miss (regenera), nunca quebra o jogo. `report()` expõe
  opfs/hits/misses/writes/erros/bytes.
- `src/wwwroot/js/game.js`:
  - `game.init` virou async: aquece o cache (texturas + terreno) ANTES de
    construir a cena (`warmTextures`/`warmTerrain`); `window.__initMs` registra
    o tempo total do init.
  - Texturas procedurais passam por `proceduralTexture(name, size, generate)`:
    miss gera como antes e grava (async, fora do caminho crítico); hit aplica o
    RGBA (+ heightmap `_h`) direto no DynamicTexture. Efeito colateral bom:
    visitas ficam determinísticas (a grama desenhada com `Math.random` passa a
    vir do cache).
  - `buildTerrain` usa posições/cores/normais prontas quando o warm carregou;
    senão computa como antes e grava para a próxima visita. `saveTerrain`
    aceita Float32Array ou Array comum (`getVerticesData` do Babylon devolve
    array normal — causa do único bug encontrado, ver Validação).
  - `window.game` agora **mescla** (`Object.assign`) em vez de sobrescrever —
    `assets.js` registrava `game.assets` antes e era perdido pelo game.js.
- `src/wwwroot/index.html`: carrega `assets.js?v=__BUILD__` antes do game.js.
- O cache é pura otimização: a verificação de SHA-256 das DLLs em C#
  (`ModelLoader`) e o fallback de marcadores não foram tocados. Sem OPFS
  (Firefox/Safari antigos), tudo gera na hora como antes.

## Validação e entrega

- Local (build publicado, Chromium headless): visita fria gravou 6 artefatos
  (2,1 MB); reload carregou 6/6 com `initMs` 114 → 47. Bug encontrado no
  caminho: `saveTerrain` quebrava com `TypeError` (positions do Babylon é Array
  comum, `.buffer` undefined) — corrigido com `Float32Array.from` e testes
  verdes.
- Corrupção: arquivo do terreno no OPFS sobrescrito com 64 bytes de lixo →
  reload registrou 1 erro de validação, cache miss, terreno recomputado e
  regravado, 5 texturas servidas do cache intactas, mundo renderizando.
- `dotnet publish -c Release` sem erros; publicado no push `bff547f`;
  workflow do Pages concluído com sucesso.
- Produção (https://sufficit.github.io/sufficit-game-ultima/, build
  `bff547f2…`): visita 1 — OPFS ativo, 6 gravações, `initMs` 106; visita 2
  (reload, mesmo contexto) — 6 acertos do cache, 0 gravações, `initMs` 41
  (**-61%**); teleport para a Guardiã, diálogo abre, resposta chega com origem
  marcada (validação de ponta a ponta com o mundo cacheado). Screenshot em
  `/tmp/ultima-opfs-prod.png` na sessão de validação.
- Ressalva: medido em headless (SwiftShader); o ganho medido é de CPU de
  geração (init), não de render — em GPU real o efeito percebido é o mesmo
  tempo de init economizado.
- Decisão registrada: `?v=sha256` + Cache API para as DLLs descartado por ora
  (o loader C# já usa `?h=<sha256>` e o Pages serve por commit); reabrir se as
  DLLs crescerem.
