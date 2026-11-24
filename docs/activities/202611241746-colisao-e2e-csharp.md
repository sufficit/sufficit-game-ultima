# Migração do teste de colisão do mundo para C#

## Investigação

O teste legado dirigia o Chromium via Playwright para Node.js e exercitava o
mesmo jogo publicado nos modos worker e thread principal. O inventário cobriu
contato entre avatares, tropeço, passagem por corredores, marcha e giro, apoio
e repouso, colisão do cenário, diálogo e ciclo de vida dos avatares remotos.

## Alterações

- O [runner C#](../../tests/WorldCollisionE2E) usa Microsoft.Playwright com
  .NET 10. O runner mantém os cenários, comandos de teclado, métricas, limites
  e diagnóstico de falhas em C#.
- As sondas JavaScript coletam transformações, amostras por quadro,
  interseções e outros dados do Babylon.js na página ou no worker. As
  exportações do jogo usadas para preparar cada cenário continuam acessíveis
  pelos mesmos pontos de entrada.
- O script legado saiu após a validação em ambos os modos. Os links nas
  atividades anteriores apontam para a revisão histórica. Os comandos de
  instalação e execução estão na [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração).

## Validação e entrega

- A suíte C# passou no Chromium em worker e main no [site publicado](https://sufficit.github.io/sufficit-game-ultima/),
  com o SHA exigido `85d587cc3ae9b2cda02888721b52df8e072c478d`. A
  suíte antiga também completou ambos os modos nesse SHA. Uma execução
  adicional do legado em main mediu 0,140106 m no contato lateral, acima
  do limite de 0,14 m. O limite foi preservado.
- Os dois modos na suíte C# mediram 3,5 m/s na caminhada e 6,5 m/s na
  corrida. As solas ficaram a cerca de 4 mm da superfície nas sete posições
  amostradas sobre o tronco baixo e nas bordas.
- Passaram o build Release da suíte, o restore com lock file, o RulesSmoke
  e o publish Release do jogo.
