# Curvas nas bordas das escavações do Ultima

## Investigação

O V da captura podia ser reproduzido com uma escavação de seis camadas na
junção dos blocos. A paleta e as normais coincidiam nas divisas. A malha de
0,50 m, porém, atravessava a curvatura com faces longas. A diferença
máxima entre a altura desenhada e a superfície calculada foi de 1,14 m.

## Alterações

- A malha das células tocadas por uma escavação, escultura ou gruta passa
  a amostrar o relevo a cada 12,5 cm. As células de transição compartilham
  os mesmos vértices na borda subdividida, inclusive entre blocos.
- O restante do mapa conserva a grade de 0,50 m. Pintura atualiza somente
  a textura. Remover uma edição restaura a malha de base na região.
- A mira usa a face que está sendo desenhada. As escavações existentes
  reaproveitam o formato de armazenamento e são refinadas ao carregar.

## Validação e entrega

- No [ensaio de emendas](../../tests/terrain-seams.e2e.cjs), a diferença
  máxima da parede caiu de 1,14 m para 0,155 m. As amostras de cor,
  posição e normal coincidiram nas duas divisas testadas. A grade de base
  permaneceu em 4.225 vértices por bloco.
- A captura comparativa mostrou uma parede mais arredondada. O teste
  removeu uma escultura compartilhada e confirmou a volta à malha base.
- Publish Release, sintaxe JavaScript, `git diff --check`, modo procedural,
  [mochila](../../tests/backpack.e2e.cjs) e [colisão](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs)
  passaram localmente.
- O build `02e41e0` passou em produção nos renderizadores main e worker.
  O teste de coleta confirmou a mira alinhada em escala 2× e a recarga de
  escavações já salvas. [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45273656605).
