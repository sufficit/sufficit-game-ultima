# Passo articulado sobre obstáculos baixos no Ultima

## Investigação

A captura do mantenedor mostrava o jogador parado diante de um tronco baixo.
A colisão anterior tratava a base do tronco como uma parede, e cada perna
do aldeão estava fundida em uma única malha. Na versão anterior, o ensaio
parava a corrida em Z = 1,1 m, com o tronco a 2 m e sem ganho de altura.

## Alterações

- O movimento permite passar automaticamente sobre objetos apoiados com topo
  até a altura do joelho, aproximadamente 40 cm para o avatar de 1,75 m.
  O corpo sobe e desce com a área de apoio, em velocidade menor sobre o
  objeto. Troncos em pé e obstáculos mais altos preservam a colisão.
- Cada perna do jogador tem pivôs de quadril, joelho e tornozelo. A pose é
  calculada pelo deslocamento e pela altura sob cada pé, com alternância
  do apoio. O sapato se mantém nivelado enquanto o joelho dobra.
- A articulação é criada com o corpo inicial e reaproveita o material do
  modelo quando ele chega. Permanece local ao avatar controlado no navegador.

## Validação e entrega

- Publish Release, RulesSmoke, quatro testes Node, sintaxe JS e
  `git diff --check` passaram.
- O [ensaio de colisão](../../tests/world-collision.e2e.cjs) reproduziu a
  falha anterior e passou no Chromium WebGL2 em worker e main do build
  `fe14382`. No main, a altura do corpo subiu de 0,08 para 0,39 m, um
  joelho chegou a 1,27 rad de flexão e a diferença entre as alturas dos
  pés chegou a 14 cm. O jogador prosseguiu e voltou ao chão.
- Uma captura lateral do build publicado mostrou o pé dianteiro elevado
  sobre o tronco e o joelho dobrado. O mesmo tronco, em escala acima do
  joelho, bloqueou a corrida. Pinheiro, pedra, toco, vegetação baixa,
  inspeção e conversa com a Guardiã passaram na regressão.
- Com a DLL atrasada no teste local, o corpo inicial passou pelo tronco e
  manteve a articulação após a chegada do modelo. Os testes usaram
  alterações locais ao navegador, em uma sessão sem rede de jogo.
- Publicado na main e validado no [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45254685226).
