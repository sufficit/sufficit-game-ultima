# Não sobreposição dos corpos durante a animação

## Investigação

A varredura anterior usava a pose no início do quadro. Os braços
podiam se cruzar depois que a animação atualizava essa pose, mesmo
com os personagens parados. O Chromium reproduziu a interseção
entre os braços dos modelos visíveis.

## Alterações

- A arquitetura registra a regra **Nenhum corpo ocupa o mesmo lugar
  no espaço**. Ela vale durante o deslocamento e durante a animação.
  [Regras de física](../../ARCHITECTURE.md#regras-de-física-dos-corpos)
- As caixas orientadas dos personagens próximos são coletadas depois
  de aplicar as poses. O C# resolve as interseções antes do desenho
  e usa as caixas corrigidas para o movimento do quadro seguinte.
- A busca por separação entre poses tem resolução de 1 cm. Ela dá
  preferência à correção dos remotos, preservando a Guardiã fixa e
  o apoio do jogador quando há um remoto que pode se afastar.
  A posição local corrigida é compartilhada com a interpolação do
  avatar e com seu rótulo. [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 154 verificações C# passaram. O módulo WASM e o cliente Blazor
  foram compilados em Release.
- No teste com os dois personagens imóveis e braços sobrepostos, a
  referência manteve a interseção. A versão nova separou as caixas
  dos braços preservando a posição do jogador e manteve a separação
  durante os oito quadros seguintes. A caminhada, o contorno, o
  tropeço, a subida no tronco e o diálogo também passaram.
- O commit `0c9b6351133ed23de469f7fe10cd72316493863c` passou no GitHub Pages
  no worker e na thread principal. [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

A resolução usa caixas orientadas como aproximação das malhas e
separação horizontal na representação local dos personagens próximos.

No A/B com 64 avatares em movimento no Chromium WebGL com GPU AMD,
o p95 do laço de CPU foi de 9,8 → 13,7 ms e o p95 do
intervalo entre quadros foi de 18,4 → 18,9 ms. Cada cenário
usou 90 amostras em uma bancada.
[Harness de desempenho](https://github.com/sufficit/sufficit-game-ultima/blob/865ca328adf67db9e5d172997ad05e4632eb6a20/tests/remote-performance.cjs)
