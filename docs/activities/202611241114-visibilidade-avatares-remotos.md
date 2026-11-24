# Visibilidade dos avatares remotos distantes

## Investigação

A otimização anterior reduziu o mapa de sombras de avatares distantes.
O Babylon.js ainda avaliava os objetos de cena dos avatares fora da
câmera. [Medição das sombras](202611241105-sombras-avatares-remotos.md)

## Alterações

- O Babylon.js testa o volume visível após resolver posição, órbita e
  colisão da câmera para o quadro. Usa uma esfera em torno do corpo e
  do rótulo, com 2 m para entrar e 2,5 m para sair.
- Desativa as duas representações dos remotos que estão no nível distante
  e fora da câmera. O lote C# continua atualizando movimento e pose
  durante esse período. Ao voltar ao campo de visão ou entrar no nível
  próximo, o avatar reaparece com a posição e a pose atuais.
- O nível próximo conserva a participação no mapa de sombras mesmo atrás
  da câmera. A matriz e os seis planos são reaproveitados e calculados
  uma vez por quadro com avatares distantes.
  [Arquitetura](../../ARCHITECTURE.md)

## Validação e entrega

- O teste de cena falhou na referência ao exigir a desativação do avatar
  atrás da câmera. A candidata passou no worker e na thread principal,
  incluindo reaparecimento e sombras ao se aproximar.
- Publicação Blazor Release e 129 verificações de `RulesSmoke` concluídas.
  O commit `d9ddf67cdfd6090fa578f34d933cd1bd64f3de05` passou no teste
  de cena em produção nos dois modos. [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)

### Comparativo A/B

Chromium WebGL no worker, a 640 × 360, com os builds `c0908cd` e
`d9ddf67`. Cada cenário teve 90 amostras após aquecimento. Metade dos
avatares ficou no nível próximo e metade ficou distante, fora da câmera.
O p95 do laço mede CPU até a chamada a `scene.render()` retornar.
O intervalo entre quadros também inclui o agendamento do navegador.
As duas métricas estão em milissegundos.
[Harness de desempenho](../../tests/remote-performance.cjs)

| Renderer | Avatares | Estado | Ativos no Babylon.js | p95 do laço | p95 do intervalo |
| --- | ---: | --- | ---: | ---: | ---: |
| SwiftShader | 12 | Repouso | 12 → 6 | 6,8 → 6,6 | 62,8 → 61,3 |
| SwiftShader | 12 | Movimento | 12 → 6 | 8,0 → 6,0 | 77,2 → 65,2 |
| SwiftShader | 32 | Repouso | 32 → 16 | 20,9 → 8,4 | 181,6 → 79,8 |
| SwiftShader | 32 | Movimento | 32 → 16 | 10,1 → 8,2 | 86,0 → 79,4 |
| SwiftShader | 64 | Repouso | 64 → 32 | 15,7 → 12,0 | 114,2 → 110,7 |
| SwiftShader | 64 | Movimento | 64 → 32 | 16,5 → 12,5 | 112,8 → 109,9 |
| AMD integrada | 12 | Repouso | 12 → 6 | 6,5 → 5,6 | 18,5 → 18,5 |
| AMD integrada | 12 | Movimento | 12 → 6 | 7,1 → 5,3 | 20,0 → 18,4 |
| AMD integrada | 32 | Repouso | 32 → 16 | 17,4 → 7,2 | 26,3 → 18,8 |
| AMD integrada | 32 | Movimento | 32 → 16 | 9,0 → 7,0 | 18,9 → 18,7 |
| AMD integrada | 64 | Repouso | 64 → 32 | 18,9 → 11,5 | 25,9 → 19,8 |
| AMD integrada | 64 | Movimento | 64 → 32 | 24,0 → 13,3 | 31,7 → 20,3 |

O resultado depende da quantidade de avatares distantes fora da câmera.
O teste registra uma sessão por renderizador na mesma bancada. Na GPU
AMD, a cadência já ficava perto de 60 quadros/s em várias amostras.
