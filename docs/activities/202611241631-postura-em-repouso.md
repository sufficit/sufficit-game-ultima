# Estabilidade da postura em repouso

## Investigação

No Chromium, o tronco continuava oscilando depois de três paradas.
A raiz e os tornozelos praticamente já estavam imóveis. A oscilação
vinha da rotação periódica do tronco aplicada à pose neutra do jogador.
A simulação também permitia renovar a reação a um passo cruzado
enquanto a fase da passada estava parada.

## Alterações

A pose neutra do jogador estabiliza o tronco. O pé em balanço termina o
apoio no ponto onde parou, reduzindo gradualmente altura e inclinação.
A separação horizontal entre as solas continua ativa e só inicia uma
nova reação de tropeço enquanto o jogador se desloca ou gira.
[Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- As 162 verificações C# passaram. Os testes cobrem o pouso do pé em
  balanço, a retomada gradual do passo e a pose neutra do tronco.
- A versão anterior falhou na medição de oscilação após três paradas.
  A correção passou nessas três posições e no repouso sobre um tronco.
  [Teste de mundo e marcha](../../tests/world-collision.e2e.cjs)
- Motor WASM e cliente Blazor compilados em Release. Os testes de
  colisão e contato do pé passaram no Chromium no worker e na thread
  principal, incluindo o build `d1023d17081ee557950dabda88453c23f2e75620` no site publicado.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

Nos quatro cenários publicados, a variação medida da inclinação do
tronco após o repouso foi zero nos dois modos.
