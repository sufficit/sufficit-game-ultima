# Inversão do jogador no próprio eixo

## Investigação

A inversão acionava uma passada proporcional ao giro do corpo. O pé
chegava a 58 cm da raiz no trecho central do giro observado no Chromium.
A separação dos pés também aumentava mesmo com o corpo quase parado.

## Alterações

O C# mantém a raiz no mesmo ponto durante uma inversão fechada.
O quadril conduz o giro e o tronco acompanha com pequena defasagem.
Os pés alternam apoio em arcos curtos próximos ao eixo. A sola em apoio
gira sobre o ponto de contato. Ao concluir, a marcha usa a posição atual
dos pés para iniciar a próxima passada. Curvas de menor amplitude
continuam usando a marcha existente.
[Guia de animação](../USAGE-ANIMACOES.md)

## Validação e entrega

- 168 verificações em C#, incluindo inversão para os dois lados,
  alternância de apoio e largura da base.
- Testes no Chromium de inversão, caminhada, corrida, repouso,
  colisão, apoio e encurtamento da passada.
  [Teste de mundo e marcha](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs)
- Motor e cliente compilados em Release. O build `3d324f6cada61aed91b591e0b7aa3ca37501090f`
  passou no site publicado no worker e na thread principal.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)

Os comandos de movimento do teste passaram a usar tempo de simulação.

- worker. Alcance de 17,5 cm do pé em relação à raiz e separação de 26,9 cm.

- main. Alcance de 16,5 cm do pé em relação à raiz e separação de 26,4 cm.

O deslocamento horizontal da raiz ficou abaixo de 1 mm no trecho medido nos dois modos.
