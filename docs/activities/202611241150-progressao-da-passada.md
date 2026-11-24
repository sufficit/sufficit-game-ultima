# Progressão da passada e coordenação dos braços

## Investigação

O corpo avançava no sentido do comando. Após soltar a sola do chão, o
pé ainda seguia uma mistura entre o último apoio e um alvo relativo
ao quadril. Essa combinação atrasava o começo do balanço e fazia a
perna recuar visualmente em relação ao corpo. O teste mede essa fase.
[Apoio mundial dos pés](202611241138-apoio-mundial-dos-pes.md)

## Alterações

- Durante o balanço, o pé do jogador percorre uma trajetória entre o
  último apoio e a próxima aterrissagem prevista. A aceleração na
  saída é mais curta. A desaceleração acontece antes de tocar o chão.
- O ciclo de marcha do jogador passou de 0,96 m a 1,16 m por ciclo.
  Os braços do jogador chegam à amplitude na fase oposta à da perna
  do mesmo lado. [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- As 141 verificações C# passaram. Publicação Release do motor WebAssembly
  e do Blazor concluída.
- Na referência, o Chromium mediu 10,6 cm de recuo total durante a saída
  do apoio, dos quais 5 cm excederam o avanço do corpo entre as duas
  amostras que cercaram a transição. Na candidata, o recuo adicional
  medido foi zero. A compensação cobre a incerteza sobre o quadro em que
  o pé efetivamente saiu do chão.
- Caminhada, corrida, curva, inversão, parada e apoio no tronco passaram
  em ambos os modos. O commit `24c71c3a0495b651014160bb9fd24e0aae0956ae`
  foi validado no GitHub Pages com execução no worker e na thread principal.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)
