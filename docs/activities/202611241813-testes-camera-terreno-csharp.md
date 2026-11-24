# Migração dos testes de câmera e terreno para C#

## Investigação

Três suítes JavaScript exercitavam câmera e streaming, cache OPFS de malhas
e emendas do terreno no Chromium. A comparação usou o mesmo SHA do jogo
em worker e na thread principal. O teste de emendas também executou
com WebP e com materiais procedurais.

## Alterações

- Os cenários `camera-streaming`, `terrain-cache` e `terrain-seams` usam
  o [runner Microsoft.Playwright](../../tests/WorldCollisionE2E). As sondas
  consultam a câmera, a geometria e os arquivos no navegador.
- As medições de cor, encaixe das bordas e erro da parede são calculadas em
  C#. A sonda de cor envia faixas de até dois texels junto de cada divisa
  para permitir a filtragem bilinear sem transferir a textura inteira.
- Os scripts antigos saíram após a comparação. Os registros anteriores
  apontam para a revisão histórica. A [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração)
  registra os comandos e as opções.

## Validação e entrega

As oito combinações passaram em ambas as versões no Chromium contra o
build `fdf37ed6cba91ec88e94a0cbc7b227182c1cf693` do
[site publicado](https://sufficit.github.io/sufficit-game-ultima/).

- A órbita livre de raio 7,5 recuou para cerca de 1,25 diante das árvores e
  voltou ao raio original. Arrasto, roda e streaming de pelo menos 25
  chunks sem duplicatas passaram.
- O cache passou na geração fria, no reuso pelo OPFS, na regeneração após
  edição e na recuperação após corrupção. A malha base recuperou as
  mesmas posições medidas. O orçamento de renderização lido foi de 4 ms.
- As métricas de emendas coincidiram com o legado. Alturas e normais
  tiveram desvio zero na divisa. A parede escavada teve erro máximo de
  0,154892 m sob o limite de 0,16 m. A escultura remota refinou a malha e
  sua remoção restaurou os 4.225 vértices da malha base.
- Passaram o build Release da suíte, o restore com lock file, o RulesSmoke
  e o publish Release do jogo.
