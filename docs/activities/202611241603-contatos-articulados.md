# Resposta articulada antes da separação dos corpos

## Investigação

O motor anterior corrigia a interseção após atualizar a pose, deslocando
a raiz de um dos personagens. Na reprodução com os dois personagens
parados, o braço liberava a interseção ao custo de afastar um deles.
[Não sobreposição dos corpos](202611241300-nao-sobreposicao-dos-corpos.md)

## Alterações

- Os braços humanoides são renderizados em dois segmentos, ligados
  pelo cotovelo. As mãos acompanham os punhos.
- O C# identifica as caixas em contato por membro. O motor testa
  retração de ombro e flexão de cotovelo e conserva apenas poses que
  liberam o contato. Dois braços podem se recolher no mesmo quadro.
- O pé do jogador em balanço pode adotar um passo menor. A solução
  recalcula a articulação com a altura no novo ponto e preserva a raiz.
  Encurtamento superior a 12 cm com velocidade acima de 2,4 m/s
  aciona tropeço. Um choque corporal tem prioridade sobre essa reação.
  [Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 158 verificações C# passaram. O motor WebAssembly e o cliente
  Blazor foram compilados em Release.
- A referência falhou no teste que exige separar os braços com as duas
  raízes no lugar. A candidata preservou os 0,63 m entre centros em
  oito quadros de gestos repetidos.
- A fixture do pé em balanço posicionou uma malha remota na trajetória
  de um passo de 38 cm. O solver recolheu os 38 cm sem deslocar a raiz.
  [Teste de passo articulado](../../tests/articulated-step.e2e.cjs)
- Caminhada, contorno, diálogo e choque corporal passaram nos dois
  modos. Os testes em produção verificaram o commit `e7aa93126ba29173237cfccb66e12bc6643a0408`
  no worker e na thread principal. [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

Os ajustes são cinemáticos e usam as caixas aproximadas das malhas
próximas. A guarda de passo atua no jogador e no pé em balanço.
Se as tentativas locais falham, a resolução afasta os corpos.

No A/B com 64 avatares em movimento, WebGL no Chromium com GPU AMD
e 90 amostras por cenário, na comparação entre `0c9b635` e a versão entregue, o p95 de CPU foi de 12,4 → 11,1 ms e o
p95 do intervalo entre quadros foi de 21,3 → 20,3 ms. O mapa
de sombras recebeu 480 → 544 malhas remotas, com os antebraços separados.
Com 64 avatares parados, o p95 de CPU subiu de 9,6 para 13,6 ms e o
intervalo entre quadros subiu de 18,8 para 19,3 ms. Cada cenário foi
medido em uma rodada.
[Harness de desempenho](../../tests/remote-performance.cjs)
