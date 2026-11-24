# Apoio mundial dos pés do jogador

## Investigação

O recuo do pé em relação ao corpo compensava o avanço em linha reta,
mas girava junto com o jogador ao trocar de direção. O Chromium mediu
7,2 cm de deslocamento da sola em apoio entre quadros numa curva de 90°.
[Migração anterior da marcha](202611241125-anatomia-e-marcha.md)

## Alterações

- O C# guarda posição horizontal e rumo de cada sola em coordenadas
  mundiais durante o apoio. Durante o balanço, o pé se desloca para
  o próximo contato. O giro do corpo pode iniciar um passo mesmo no lugar.
- A solução das pernas inclui inclinação lateral do quadril. O Babylon.js
  compensa as rotações do quadril e do joelho no tornozelo para preservar
  o rumo da sola em apoio. A sondagem de altura considera os contatos
  calculados e continua usando a superfície dos obstáculos.
- Reinício e teleporte liberam as âncoras e retornam à fase neutra.
  O contato mundial cobre o jogador controlado.
  [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 139 verificações C# concluídas. O módulo de terreno WASM e o cliente
  Blazor passaram no build Release.
- O Chromium reproduziu a derrapagem na curva no build anterior.
  Na candidata, o deslocamento da sola apoiada foi inferior a 0,01 mm
  por quadro nos testes de curva, inversão e parada. Os dois modos também
  passaram nas verificações de marcha reta, diálogos e apoio em obstáculos.
  O teste de coleta passou localmente nos dois modos.
- O GitHub Pages serviu `a003f668f2c9d311fc2e7c684749b54cdea5cad2` e
  passou nos testes de cena no worker e na thread principal.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

A sondagem atual usa um ponto de altura por sola. Inclinar a sola pela
normal da superfície requer amostras adicionais de contato. Os avatares
remotos e o Andarilho usam o solver sagital.
