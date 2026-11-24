# Apoio dos pés em troncos baixos no Ultima

## Investigação

A captura do mantenedor mostrava as duas solas suspensas junto a um tronco
baixo. A elevação do avatar usava o topo da caixa de colisão, com uma margem
ao redor do objeto. O teste sobre o build `ee2f5d2` mediu até 34 cm de
distância entre uma sola e a superfície visível.

## Alterações

- Cada sapato encontra seu apoio pela interseção vertical com a malha real
  do obstáculo ou pela altura do solo na mesma posição.
- Nas bordas, o quadril só sobe até onde a perna que fica mais baixa alcança.
  Assim, um pé pode apoiar no chão enquanto o outro se apoia na madeira.
- O critério de altura do joelho preserva o bloqueio de troncos altos. A
  marcha ainda eleva o pé em movimento e dobra a perna para transpor o tronco.

## Validação e entrega

- Publish Release concluído, com os avisos de nulabilidade já existentes em
  `Index.razor`. A sintaxe JavaScript e o `git diff --check` passaram.
- O [ensaio de colisão](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs) validou a subida,
  o retorno ao solo e o bloqueio de um tronco alto. A medição das duas solas
  em sete pontos confirmou apoios no chão e no tronco, inclusive na borda.
- Uma captura lateral local confirmou visualmente as duas alturas de apoio.
- O build `d0b7cef` passou no [jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)
  com WebGL em main e worker. O vão máximo foi de 4,7 mm. Durante a
  passagem, o ensaio mede a subida do tornozelo, pois o quadril pode
  ficar baixo quando o outro pé ainda está apoiado no chão.
- O [ensaio da mochila](../../tests/backpack.e2e.cjs) também passou em
  produção por toque. [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45259641716).
