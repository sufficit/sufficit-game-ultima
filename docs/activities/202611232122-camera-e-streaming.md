# Câmera orbital e streaming do terreno

## Investigação

A câmera orbital, a grade de 32 m e a fila de modelos já estavam em uso.
A câmera ainda chegava ao raio solicitado pela roda mesmo quando um sólido
estava entre ela e o personagem. A janela de chunks podia enfileirar a mesma
coordenada duas vezes antes de concluir a malha e arredondava pela borda errada.

## Alterações

- O trace injetado por mundo verifica faces dos sólidos e tetos de gruta,
  e amostra a superfície visível em degraus de 25 cm. A câmera recua ao
  encontrar um bloqueio e recupera o zoom pedido de forma amortecida.
- Arrasto, roda e pinça passam pela mesma ponte. O teste de emendas anterior
  comprova a continuidade das alturas e normais nas divisas.
- Janela centrada no chunk real do jogador, descarte fora da zona de
  retenção, limite de um chunk por frame e deduplicação da fila. A densidade
  da névoa usa o alcance da janela, inclusive durante chuva.

## Validação e entrega

- Publish Release, testes de mochila, colisão, comandos e emendas passaram.
- O [ensaio de câmera e streaming](../../tests/camera-streaming.e2e.cjs)
  passou no Chromium em worker e modo principal, inclusive no build
  `7aede38` no GitHub Pages. O raio de 7,50 m recuou a 1,25 m em um
  grupo de pinheiros e voltou a 7,50 m depois de removê-los. A janela
  descartou o chunk de origem e as malhas carregadas tinham chaves únicas.
- A malha em C# e os testes anteriores de escavação estão documentados
  na [atividade de terreno WebAssembly](202611231906-terreno-csharp-wasm.md).
  [Jogo](https://sufficit.github.io/sufficit-game-ultima/)
  · [Publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45295909758).
