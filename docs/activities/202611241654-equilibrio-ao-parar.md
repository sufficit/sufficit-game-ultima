# Equilíbrio do jogador ao parar

## Investigação

A pose anterior nivelava as solas em repouso, mas conservava os alvos
horizontais do último quadro da marcha. Em três paradas no Chromium,
a distância horizontal entre o quadril e o tornozelo chegou a 29 cm.
Isso podia deixar o tronco atrás dos pés mesmo com a animação já parada.

## Alterações

O C# usa a última posição de cada sola para reposicioná-la sob o
quadril. O pé que já está mais elevado se acomoda primeiro. Cada ajuste
faz um passo curto e preserva o outro apoio. Os dois alvos continuam
passando pela sondagem do terreno e dos objetos. Se o jogador voltar
a andar durante o ajuste, a marcha parte do último apoio calculado.
[Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- As verificações C# passaram, incluindo um pé imóvel de cada vez,
  chegada à base neutra e retomada gradual da passada.
- A versão anterior falhou na asserção de alinhamento em repouso.
  A correção passou nos cenários de parada, caminhada, corrida,
  inversão de rumo e apoio sobre tronco.
  [Teste de mundo e marcha](../../tests/world-collision.e2e.cjs)
- Motor e cliente compilados em Release. Os testes passaram no
  worker e na thread principal no site publicado, no build
  `414711ebea360b8186b56bc71ebb4ded9e06f304`.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

Nas três paradas em terreno plano, o desvio horizontal entre o tornozelo
e o quadril ficou abaixo de 1 mm nos dois modos após o ajuste.
