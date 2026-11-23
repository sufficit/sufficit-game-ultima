# Câmera mantém a distância escolhida na roda; E só conversa perto do personagem

## Investigação

**Relatos do mantenedor:** (1) "a câmera deve acompanhar o personagem na proporção de
distância que o jogador definir no scroll, não está acontecendo"; (2) "em qualquer lugar
do mapa que eu aperto o E inicia a conversa com o andarilho".

Medindo o raio da câmera (distância ao personagem) com um roteiro que dá zoom e anda,
nos dois modos de render:

| | raio após 7 entalhes de zoom | depois de andar 2 s |
|---|---|---|
| sem worker | 3,70 | **7,09** |
| com worker | 6,99 (zoom quase sem efeito) | **10,01** |

Causas:

- **Câmera.** O seguidor chamava `camera.setTarget()` a cada frame. No Babylon 9.36 o
  `setTarget` recalcula o raio a partir da posição atual da câmera; com o personagem
  andando, o raio rangueava de volta para perto do inicial. Além disso, no worker a roda
  enviava só o sinal do movimento (±1% por evento) e o zoom por pinça tinha se perdido na
  migração (era feito pelo `attachControl` do Babylon, que não existe no worker).
- **E do Andarilho.** A reescrita da interface (`7a9e45d`) trocou a proximidade dele por
  "há jogadores remotos na sala" (`PlayerNearWanderer() => _remoteCount > 0`): com
  qualquer remoto, E abria a conversa em qualquer lugar; sem remotos, o Andarilho do
  mundo não conversava nem com o jogador do lado.

## Alterações

Commit `0892fd3` (rebase sobre `70720cd`):

- `game-core.js`: o seguidor muta o vetor-alvo no lugar (`camera.target.set`), o que
  preserva alfa, beta e o raio escolhido; o teleporte para a Guardiã faz o mesmo.
- Zoom do worker com a mesma proporção do Babylon com `attachControl` (um entalhe afasta
  ≈12,1%; para perto, 7 entalhes ≈ −50%), chegada suave por tempo real, `deltaY` completo
  e fator 40 quando a roda vem em linhas.
- Pinça no modo worker (`game.js`): dois dedos, mesma convenção do Babylon (juntar os
  dedos aproxima); reduzir a distância à metade ≈ −50% de raio. Arrasto com a mesma
  sensibilidade do `attachControl` (0,01 rad/px).
- `Index.razor`: volta a usar o evento real de proximidade do motor
  (`OnWandererNear` → `_nearWanderer`); a dica mostra "Perto do Andarilho"; o Andarilho
  responde com as falas próprias dele.

Achado e corrigido no caminho (commit `68ee6c7`): o `saveChunk` do world-builder
(`bd8d7ad`) declarava as partes do artefato em elementos em vez de bytes — a leitura
rejeitava o chunk, que era regravado errado a cada visita. Corrigido em commit próprio.

Conflito resolvido no rebase: a outra frente acrescentou o clique curto (edição do
mundo) no mesmo trecho de ponteiro em que entrou a pinça; os dois foram unidos (arrasto
gira, dois dedos dão zoom, clique curto de um dedo segue editando o mundo).

## Validação e entrega

Build publicado e depois a produção (build `68ee6c7`), lendo o estado do motor:

| | com worker | sem worker |
|---|---|---|
| 1 entalhe para perto | 7,50 → 6,76 (−9,85%) | 7,50 → 6,70 (−10,64%) |
| 7 entalhes para perto | 3,63 (−51,6%) | 3,82 (−49,1%) |
| **andar 2 s depois do zoom** | **3,63 (0,00% de deriva)** | **3,82 (0,00%)** |
| 10 entalhes para longe | ×3,14 | ×3,12 |
| giro por um dedo (200 px) | 2,00 rad | 2,00 rad |
| pinça juntando (−50%) | 7,50 → 3,70; andar: 0,00% | — |

- E: longe dos personagens não abre nada (com e sem outros jogadores na sala — testado
  com 11 a 14 remotos, três tentativas cada); perto do Andarilho abre o diálogo dele e
  ele responde com as próprias falas.
- Fluxo do jogo (Guardiã, Andarilho, cache local), E com remotos e layout repetidos
  no build mesclado com o world-builder e na produção: todos verdes.
- Cache de chunks depois da correção: 15 acertos, nada regravado, zero erros na 2ª e na
  3ª visita (antes: 1 erro e 1 regravação por visita).

**Pendência entregue à frente do world-builder:** com o aquecimento por proximidade
assíncrono (`prefetchChunkCache`, `26ae1ba`), os primeiros chunks são preenchidos antes
de o cache chegar e acabam recomputados e regravados (1 gravação por visita nas medições).
É desperdício, não falha.
