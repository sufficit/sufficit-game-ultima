# Precisão dos cliques e escavação em camadas no Ultima

## Investigação

O build anterior falhou ao coletar um toco visível no ensaio com escala de
exibição 2×. A ponte convertia o clique em pixels do buffer, e a seleção
do Babylon aplicava a escala mais uma vez. O terreno também deslocava o
corte para o centro de uma célula de 2 m e usava uma chave única por célula.
[Implementação da seleção no Babylon.js 9.36.0](https://github.com/BabylonJS/Babylon.js/blob/9.36.0/packages/dev/core/src/Culling/ray.core.ts#L753-L759).

## Alterações

- Os eventos de clique e toque chegam ao motor em coordenadas CSS relativas
  ao canvas. A seleção aplica a escala do renderizador.
- O corte começa nas coordenadas reais atingidas na malha. Cada coleta de
  terreno tem seu próprio registro no IndexedDB, rende um item e soma uma
  camada à escavação. A profundidade permanece limitada pelo alcance do
  personagem ao fundo ou à parede em que ele mira.
- Os registros antigos continuam contando na mochila e mantendo os cortes
  nas coordenadas que já estavam salvas. O jogador pode aprofundá-los.
  A deduplicação por identificador continua para objetos coletáveis.

## Validação e entrega

- Publish Release, sintaxe JavaScript, `git diff --check` e o
  [ensaio de colisão](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs) local passaram.
- O [ensaio da mochila](../../tests/backpack.e2e.cjs) verificou coleta de
  um toco, alcance, três cortes no mesmo ponto fora do centro da grade,
  acréscimo de três itens, recarga e extensão de um corte legado.
  A profundidade medida foi de cerca de 1,32 m após três cliques.
- O build `775ed60` passou em produção com clique e toque em escala 2×
  nos renderizadores main e worker, além do clique em escala 1×.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45260886764).
