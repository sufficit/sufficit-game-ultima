# Integração da virada à primeira passada

## Investigação

O giro compacto deixava o quadril rodar com apoios alternados, mas o
motor só liberava a translação depois de completar a rotação. O teste
no Chromium mediu zero de avanço nos quadros centrais da inversão.

## Alterações

A velocidade diminui no início da inversão e cresce conforme o quadril
se alinha com a direção solicitada. O deslocamento usa a mesma varredura
de terreno e personagens da marcha. A última troca de apoio antecipa o
pouso na nova direção, e a perna oposta começa o próximo balanço.
[Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 169 verificações em C#, incluindo avanço antes do alinhamento e
  bloqueio por colisor durante essa transição.
- O Chromium verificou quadros com rotação e avanço simultâneos,
  base compacta, apoio plantado, caminhada e corrida.
  [Teste de mundo e marcha](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs)
- Motor e cliente compilados em Release. O build `6da44a559b6ebd91487a00c5937a059a345a3788`
  passou no site publicado no worker e na thread principal.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

- worker. Avanço de 17,8 cm em 8 quadros ainda em rotação.

- main. Avanço de 17,7 cm em 8 quadros ainda em rotação.
