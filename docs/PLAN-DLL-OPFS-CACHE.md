# Cache OPFS de assets preparados com chave SHA-256

> **Concluído em 21/11/2026** — convertido na atividade
> [202611212050-cache-opfs-preparados.md](activities/202611212050-cache-opfs-preparados.md).
> Resumo da entrega: `js/assets.js` com OPFS, chave SHA-256 de {build, escopo,
> parâmetros}, gravação `.tmp`+renome e validação de integridade; texturas procedurais
> e malha do terreno geram UMA vez e carregam prontas nas revisitas. Em produção:
> init 106 ms → 41 ms (-61%) na segunda visita; corrupção detectada vira cache miss.

## Objetivo e critérios

Estender o pipeline de DLLs de modelos (`models/Ultima.Models.*`,
`scripts/build-models.sh`, download validado por SHA-256) com um estágio de
**preparação e cache local**: após baixar e conferir a DLL, o cliente prepara o
conteúdo para uso (parse/instanciação) UMA vez e grava o artefato pronto no
**OPFS**, com chave derivada de hash de build + asset. Revisitas carregam
direto do cache, sem retrabalho.

Critérios de aceite:

- Chave de cache = SHA-256 de `{build do jogo, nome+hash da DLL, opções de
  preparação}`; mudança em qualquer componente invalida o cache.
- Publicação atômica no OPFS (gravar em temporário e renomear) com `metadata`
  contendo os hashes; leitura valida o metadata antes de usar.
- Primeira visita prepara e cacheia; segunda visita pula a preparação (comprovar
  por tempo de carga e por log de origem do asset).
- Adulteração do cache é detectada e tratada como cache miss (repreparo), sem
  quebrar o jogo.
- Comportamento atual de recusa de DLL adulterada permanece intacto.

## Terreno e referências

- Fonte: lineage-rust pré-processa mapa/colisão num worker separado e grava a
  cena pronta em OPFS com sync access handles, Web Locks e chave SHA-256.
  - `web/prepare-worker.js` (OPFS, chave de cache, `metadata.json`, locks)
  - `web/preparation-cache.mjs` (`digest`, `cacheKey`, `validMetadata`)
  - `web/prefetch.mjs` (Cache API com URL `?v=sha256`, concorrência limitada)
- Hoje no Ultima: DLLs baixadas com verificação de SHA-256 e recusa com
  fallback para marcadores (rochas/pedras/grama).

## Checkpoints

1. [concluído] Prova de conceito OPFS: artefato preparado de
   `Ultima.Models.Rocks` com chave hash e metadata. A prova foi feita com o
   artefato de maior custo real, a malha do terreno (posições/cores/normais,
   37.249 vértices), mais as 5 texturas procedurais (bark/foliage/rock/grass/
   detail, 128–256 px, com heightmap `_h` quando existe).
2. [concluído] Generalizar para Scenery e Trees; isolar em módulo próprio com
   testes de invalidação e adulteração. Os modelos em si já entram pela fila
   amortizada (frente concluída em
   [202611212030-upload-amortizado-modelos.md](activities/202611212030-upload-amortizado-modelos.md));
   o que restava cacheável do custo de entrada era exatamente textura+terreno —
   isolados em `js/assets.js` (chave, `.tmp`+renome, validação, report).
   Adulteração testada: arquivo do terreno sobrescrito com lixo → erro
   registrado, cache miss, recomputo e regravação, mundo íntegro.
3. [concluído] Avaliar URL versionada `?v=sha256` para as DLLs + Cache API —
   **decisão: descartar por ora**, com motivo: o ModelLoader (C#) já busca as
   DLLs com `?h=<sha256>` e o Pages as serve imutáveis por commit; o ganho
   marginal sobre o cache HTTP do navegador é pequeno frente ao OPFS dos
   artefatos preparados. Reabrir se as DLLs crescerem (ex.: malhas reais).
4. [concluído] Medir tempo de carga 1ª vs 2ª visita no site publicado.
   Produção (build `bff547f`, Chromium headless): visita 1 gravou 6 artefatos
   (2,1 MB, `initMs` 106); visita 2 (reload, mesmo contexto) carregou 6/6 do
   OPFS (`hits` 6, `writes` 0, `initMs` 41 — **-61%**); diálogo e resposta da
   Guardiã funcionando com o mundo cacheado.
5. [concluído] Converter este plano em atividade:
   [202611212050-cache-opfs-preparados.md](activities/202611212050-cache-opfs-preparados.md).

## Próxima ação

Nenhuma — frente encerrada. Futuro natural: gravar também as posições de
espalhamento (visitas determinísticas) quando o scatter crescer (terreno em
chunks, multidões), usando a mesma infraestrutura de chave por hash.
