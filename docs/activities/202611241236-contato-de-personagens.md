# Contato corporal e tropeço na marcha

## Investigação

Os personagens não reservavam espaço no teste de movimento. Duas poses
remotas na mesma coordenada se sobrepunham, o que o teste em Chromium
reproduziu na referência. Os sapatos também tinham uma largura maior
que o afastamento entre os pés na pose neutra.

## Alterações

- O C# verifica volumes de raio 0,54 m e altura de 1,75 m entre jogador,
  Guardiã, Andarilho e avatares remotos. A varredura restringe o
  deslocamento, e a chegada a uma posição ocupada procura espaço livre.
- A distância entre pernas na pose neutra passou a comportar as solas.
  Na marcha local, o solver mantém a ordem e a largura lateral dos
  pés. Ao interferir com a outra perna, desloca primeiro o pé no ar
  e usa um passo corretivo. Os pivôs dos braços ficam levemente abertos.
- Um choque que corta a passada ou um cruzamento relevante aciona
  recuperação cinemática. O torso inclina, os braços se preparam
  para o apoio e o jogador perde impulso. [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 148 verificações C# passaram. O módulo WebAssembly e o cliente Blazor
  foram compilados em Release.
- No Chromium, dois avatares enviados à mesma coordenada ficaram
  afastados em 1,082 m. A corrida contra um deles parou antes de
  atravessá-lo e acionou a pose de tropeço. Inversão, curva, diagonal,
  diálogo e apoio no tronco passaram no worker e na thread principal.
- O commit `efd48da844fdeca23178f2c1cdcb0cbc1f74d879` passou nesses testes
  em produção nos dois modos. [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

A resolução entre personagens usa volumes corporais conservadores.
A guarda de pernas corrige alvos de pose e o tropeço é uma reação
cinemática. Contatos individuais entre malhas arbitrárias e quedas
com dinâmica de corpos rígidos exigem um solver físico por articulação.

No A/B com 64 avatares em movimento no Chromium WebGL com GPU AMD,
o p95 da atualização de personagens passou de 0,3 para 0,6 ms e
o p95 do intervalo entre quadros passou de 18,4 para 19,2 ms.
A comparação usou 90 amostras por cenário em uma bancada.
[Harness do comparativo](https://github.com/sufficit/sufficit-game-ultima/blob/865ca328adf67db9e5d172997ad05e4632eb6a20/tests/remote-performance.cjs)
