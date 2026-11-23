# Emendas naturais no terreno do Ultima

## Investigação

As linhas da captura tinham duas origens reproduzíveis. Cada textura
terminava na própria borda, sem amostras vizinhas para a filtragem.
Além disso, a malha de 0,67 m repetia a mesma diagonal em todas as células,
visível em escavações profundas. Os materiais da trilha e do vale serviram
como referência visual junto da captura do mantenedor.

## Alterações

- Texturas passam a usar a mesma grade de amostragem em coordenadas de
  mundo e carregam uma faixa extra ao redor de cada bloco. O filtro pode
  amostrar ambos os lados das divisas. Pintura e escavação atualizam também
  as texturas vizinhas cuja faixa de amostragem foi tocada.
- Bordas de grama, terra e pintura ganham variações de contorno. As paredes
  escavadas mesclam terra e relevo mineral com pesos contínuos.
- A malha usa uma grade de 0,50 m com diagonais alternadas. A prévia de
  coleta interpola a mesma face exibida. As coletas antigas continuam sendo reaplicadas
  a partir dos registros existentes, com o mesmo raio e profundidade por camada.

## Validação e entrega

- No [ensaio de emendas](../../tests/terrain-seams.e2e.cjs), o desvio médio
  saiu de cerca de quatro níveis RGB para zero nas amostras horizontal e
  vertical, antes e depois de escavar e pintar. Posições e normais
  coincidiram nas duas divisas. A malha passou de 2.401 para 4.225 vértices
  por bloco.
- Capturas locais do mesmo corte mostraram a redução das linhas retas.
  O modo procedural passou com os arquivos de imagem bloqueados.
- Publish Release, sintaxe JavaScript, `git diff --check` e os ensaios
  locais de mochila e colisão passaram.
- O build `515e49d` passou no GitHub Pages em main e worker, incluindo o
  modo procedural em worker e a coleta por clique com escala 2×.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45270300524).
