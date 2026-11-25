# Contato de personagens junto à silhueta

## Investigação

O bloqueio anterior usava um raio fixo de 0,54 m para cada corpo.
Isso interrompia o avanço a cerca de 1,08 m entre centros, mesmo
quando ainda havia espaço visível entre os personagens. O Chromium
também reproduziu o afastamento excessivo em duas poses sobrepostas.
[Contato corporal anterior](202611241236-contato-de-personagens.md)

## Alterações

- O Babylon.js reúne caixas orientadas por malha para os personagens
  próximos. Os limites usam a pose atual, incluindo braços, pernas e
  sapatos. O C# usa varredura contínua e conserva o avanço tangencial
  no contato. Uma caixa compacta atende atores fora da região de 4 m
  do jogador ou antes de carregar a malha.
- Para poses sobrepostas, o motor procura uma posição livre perto da
  original. O impulso do torso na reação a choque aponta para trás.
  [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 152 verificações C# passaram, incluindo uma caixa girada, passagem
  entre partes desencontradas e movimento pelo lado de um contato.
  Motor WebAssembly e cliente Blazor compilados em Release.
- No Chromium, o choque de frente ocorreu a cerca de 0,50 m entre
  centros. Duas poses na mesma posição se assentaram com 0,36 m de
  separação no teste. Os dois modos passaram pela aproximação lateral,
  contorno e passagem entre avatares, caminhada reta e diagonal,
  árvore, pedra, tronco e diálogo.
- A caminhada em rampa até o interior do túnel passou localmente
  no worker e na thread principal.
- O commit `88014c7f01b4a93b5cd778dccea4e732a0b7e5f6` foi
  validado em produção nos dois modos. [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

O contato usa caixas articuladas como aproximação da silhueta.
O avanço é resolvido em translação a partir da pose amostrada.

No A/B de 64 avatares em movimento no Chromium WebGL com GPU AMD,
o p95 da atualização de personagens foi de 0,6 → 1,2 ms e o
p95 do intervalo entre quadros foi de 19,6 → 19,6 ms. Cada cenário
usou 90 quadros em uma bancada.
[Harness de desempenho](https://github.com/sufficit/sufficit-game-ultima/blob/865ca328adf67db9e5d172997ad05e4632eb6a20/tests/remote-performance.cjs)
