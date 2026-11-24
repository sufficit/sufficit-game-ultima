# Alinhamento do joelho na inversão de marcha

## Investigação

Ao inverter a marcha em 180°, a sola em apoio conservava o rumo no mundo,
mas o joelho ainda flexionava no plano do tronco. O teste mediu a
componente lateral da flexão na malha do personagem e confirmou que
ela ficava praticamente zerada mesmo durante o giro.

## Alterações

O C# calcula a rotação a partir do rumo da sola em relação ao corpo.
No rig, a coxa gira em torno do eixo quadril–tornozelo e com isso muda
o plano de flexão do joelho. O tornozelo compensa essa rotação para
preservar o rumo e a inclinação da sola. A mesma pose vale sobre
obstáculos e quando o motor encurta o passo. O ajuste usa o quadro de
animação já transmitido ao renderizador e se aplica ao jogador.
[Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 159 verificações em C# passaram. O motor WebAssembly e o cliente
  Blazor foram publicados em Release para os testes.
- A asserção de alinhamento falhou na versão de referência e passou
  com a correção. O teste mede a flexão durante a inversão, o
  deslocamento da sola plantada, a separação entre os pés e o apoio
  ao parar. [Teste de mundo e marcha](../../tests/world-collision.e2e.cjs)
- As baterias de mundo e de passo bloqueado passaram no Chromium com
  o motor no worker e na thread principal, incluindo o build
  `b0b3425389d781256f98c221f332d91ff1cda7d5` no site publicado.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

Nos dois modos, a maior deriva medida entre quadros com o pé plantado
durante a inversão ficou abaixo de 1 mm.
