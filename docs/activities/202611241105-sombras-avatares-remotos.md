# Sombras dinâmicas dos avatares remotos por distância

## Investigação

O comparativo da migração das poses mostrou que a economia no WASM nem
sempre se refletia no intervalo entre quadros. Cada avatar remoto também
mantinha no mapa de sombras as 15 malhas articuladas do corpo, mesmo depois
de passar ao nível de detalhe distante.
[Comparativo anterior](202611241052-desempenho-avatares-remotos.md)

## Alterações

- O lote WASM inclui no campo de flags o nível de detalhe já usado na
  animação. A mesma regra liga as sombras dentro de 28 m e desliga acima
  de 32 m. A faixa de histerese reduz trocas quando o avatar se move
  perto do limite.
- O adaptador Babylon.js mantém os emissores de cada personagem. A troca
  de distância atualiza apenas sua participação no mapa de sombras, com
  preservação das malhas, rótulos e poses na cena. O corpo provisório e o
  modelo carregado seguem a mesma regra. As entradas são removidas na
  desconexão.
- O jogador, os NPCs e o cenário continuam usando suas regras de sombras.
  [Arquitetura](../../ARCHITECTURE.md)

## Validação e entrega

- As 129 verificações de `RulesSmoke` passaram em Release. O build WASM
  do motor e o publish Blazor Release foram concluídos.
- O teste de cena falhou na versão anterior ao exigir que o avatar distante
  saísse do mapa de sombras. A versão candidata passou no worker e na
  thread principal, incluindo o retorno e a desconexão. Os dois modos
  também passaram no teste de sombras em cascata do perfil Cinemático.
- Os testes de cena e do perfil Cinemático passaram em produção no worker
  e na thread principal com o commit
  `a3874d1b7946eb499d1eb43c03e311790113597e`.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)

### Comparativo A/B

Chromium WebGL no worker, 640 × 360, perfil Padrão, de 12 a 64 avatares
com metade perto e metade além de 32 m. O benchmark esperou o carregamento
dos modelos, aqueceu a cena e coletou 90 quadros por cenário. As versões
comparadas foram `43155b5` e `a3874d1`. O renderer foi conferido em cada
versão. [Harness de desempenho](../../tests/remote-performance.cjs)

Os tempos são p95 em milissegundos. O laço mede o trabalho síncrono de CPU
do motor até o término da chamada a `scene.render()`. O intervalo inclui
o agendamento do navegador e reflete a cadência de quadro. A contagem de
malhas é a participação remota no mapa de sombras, que difere do número
efetivo de chamadas de desenho.

| Renderer | Avatares | Estado | Malhas no mapa | Laço | Intervalo |
| --- | ---: | --- | ---: | ---: | ---: |
| SwiftShader | 12 | Repouso | 180 → 90 | 7,3 → 7,3 | 68,4 → 63,7 |
| SwiftShader | 12 | Movimento | 180 → 90 | 6,6 → 6,7 | 68,2 → 63,6 |
| SwiftShader | 32 | Repouso | 480 → 240 | 10,4 → 10,3 | 93,9 → 82,2 |
| SwiftShader | 32 | Movimento | 480 → 240 | 10,7 → 9,6 | 94,9 → 82,0 |
| SwiftShader | 64 | Repouso | 960 → 480 | 16,6 → 15,0 | 132,6 → 113,3 |
| SwiftShader | 64 | Movimento | 960 → 480 | 18,8 → 15,8 | 138,4 → 110,4 |
| AMD integrada | 12 | Repouso | 180 → 90 | 7,1 → 5,7 | 18,7 → 18,1 |
| AMD integrada | 12 | Movimento | 180 → 90 | 5,1 → 6,0 | 17,9 → 18,6 |
| AMD integrada | 32 | Repouso | 480 → 240 | 8,6 → 7,6 | 18,8 → 18,9 |
| AMD integrada | 32 | Movimento | 480 → 240 | 9,4 → 7,9 | 19,2 → 18,4 |
| AMD integrada | 64 | Repouso | 960 → 480 | 13,5 → 11,5 | 20,0 → 19,7 |
| AMD integrada | 64 | Movimento | 960 → 480 | 15,5 → 13,5 | 21,3 → 20,7 |

O ganho mais visível no laço ocorreu com mais avatares. Os intervalos
variaram por cenário, e a avaliação mede sessões curtas em um equipamento.
