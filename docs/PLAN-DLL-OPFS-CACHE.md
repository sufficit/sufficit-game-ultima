# Cache OPFS de assets preparados com chave SHA-256

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
- Hoje no Ultima: DLLs baixadas com verificação de SHA-256 e fallback para
  marcadores quando a DLL é recusada (rocoss/pedras/grama).
- Padrão complementar deles: assets imutáveis por URL versionada
  (`?v=sha256`) — avaliar adotar junto para as DLLs.

## Checkpoints

1. [pendente] Prova de conceito OPFS: gravar/ler/invalidar um artefato preparado
   de `Ultima.Models.Rocks` com chave hash e metadata.
2. [pendente] Generalizar para Scenery e Trees; isolar em módulo próprio com
   testes de invalidação e adulteração.
3. [pendente] Avaliar URL versionada `?v=sha256` para as DLLs + Cache API
   (decidir junto com o OPFS ou descartar com motivo).
4. [pendente] Medir tempo de carga 1ª vs 2ª visita no site publicado; registrar
   números na evidência.
5. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1: prova de conceito com a DLL de rochas.
