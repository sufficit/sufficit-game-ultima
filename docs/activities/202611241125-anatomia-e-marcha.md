# Marcha sincronizada e base anatômica dos personagens

## Investigação

Durante o apoio, a curva antiga do pé avançava em função da fase e da
velocidade, mas o seu deslocamento não compensava a distância real
percorrida pelo corpo. O teste do Chromium reproduziu até 10,2 cm de
deslocamento horizontal entre quadros na caminhada em linha reta.
O solver também esticava a perna além do alcance e mantinha o pé
nivelado durante o balanço.

## Alterações

- A fase da marcha avança pela distância real. Em linha reta, a sola em
  apoio recua em relação ao quadril na mesma medida em que o personagem
  avança. O quadril ajusta a altura para manter as pernas ao alcance.
  O tornozelo inclina a sola durante o balanço e a nivela no apoio.
- O WASM fornece 73 pivôs cinemáticos para a anatomia do jogador,
  incluindo pelve, coluna, pescoço, mandíbula, ombros, cotovelos,
  punhos, quadris, joelhos, tornozelos e dedos. O Babylon.js instancia
  a hierarquia uma vez e usa os pivôs para montar o rig.
- As bases canina, equina e de ave declaram suas poses neutras com a
  mesma estrutura. As malhas e os controladores de locomoção de cada
  espécie podem consumir essa estrutura.
  [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 136 verificações C# passaram. O módulo WASM e o cliente Blazor foram
  publicados em Release.
- O Chromium no worker e na thread principal confirmou a alternância
  da inclinação dos tornozelos, a ligação de pivôs do humano e deslocamento
  horizontal inferior a 0,01 mm entre quadros nas amostras de apoio em
  linha reta. Os testes também cobriram caminhada, corrida, diálogo,
  colisão, apoio nos troncos e avatares remotos. A coleta passou localmente
  nos dois modos.
- Os dois modos passaram no GitHub Pages com o commit
  `4c70375d33377987dd3c6308559478888891d230`.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

A marcha atual resolve as pernas no plano sagital. Curvas e mudanças de
direção ainda podem mover o pé que está em apoio. As falanges, a mandíbula
e outras juntas secundárias são pivôs disponíveis para malhas articuladas
ou skinning. As bases animais descrevem anatomia e pose neutra.
