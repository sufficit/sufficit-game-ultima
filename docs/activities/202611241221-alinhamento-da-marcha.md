# Alinhamento da marcha ao mudar de direção

## Investigação

O deslocamento mudava de rumo logo que o comando mudava. A orientação
visual do personagem levava alguns quadros para acompanhar. No teste
de 90°, a velocidade perpendicular ao corpo chegou a 3,28 m/s mesmo
com o pé de apoio fixo. [Progressão da passada](202611241150-progressao-da-passada.md)

## Alterações

- O C# usa a orientação visível e o caminho permitido pela colisão
  para reduzir a translação enquanto o jogador está girando. Na
  inversão, o corpo vira antes de progredir na direção oposta.
- A distância ajustada passa por nova sondagem de colisão apenas
  durante o desalinhamento. Caminhada reta, diagonal e deslocamento
  tangente a obstáculos voltam à velocidade de regime conforme alinham.
- A amplitude lateral do tronco do jogador passou de 0,035 para 0,020 rad.
  [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 143 verificações C# passaram. O módulo WASM e o cliente Blazor foram
  publicados em Release.
- O Chromium reproduziu a translação lateral de 3,28 m/s na referência.
  A versão nova ficou abaixo de 0,8 m/s nesse cenário local. A diagonal
  atingiu 3,5 m/s com avanço equivalente nos dois eixos. Os testes
  também cobriram inversão, parada, contorno de colisores, troncos e
  contatos da sola em ambos os modos.
- O commit `e6dd0762513cf7c20f34fa5ed6c6508e2912f340` foi validado no
  GitHub Pages no worker e na thread principal.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)
