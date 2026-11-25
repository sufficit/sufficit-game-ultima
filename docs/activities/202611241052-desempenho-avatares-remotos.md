# Desempenho dos avatares remotos em C# WebAssembly

## Investigação

O build `ddc5a06` atualizava a interpolação, a pose e três amostras de altura
para cada avatar em cada quadro. A medição anterior de 12 avatares com
SwiftShader mostrou uma diferença de 0,10 ms no p95, da ordem da resolução
do cronômetro. O novo comparativo varia quantidade e movimento para medir
escalabilidade. [Medição anterior](202611241023-movimento-npc-remotos-csharp.md)

## Alterações

- O C# mantém o estado dos avatares e devolve posição, orientação e pose em
  um lote por quadro. Com 12, 32 e 64 avatares, as chamadas para eles
  passaram de 12, 32 e 64 para uma. A patrulha mantém sua chamada separada.
- A amostra de altura do centro e a de cada pé são reutilizadas quando
  coordenadas e revisão do relevo coincidem. Escavações e esculturas
  invalidam o reaproveitamento. O movimento horizontal converge para o
  destino exato quando fica a menos de 0,1 mm.
- A pose completa roda por quadro perto do jogador. O modo distante
  começa acima de 32 m, atualiza o apoio e os ossos a cada 100 ms de
  simulação e volta ao modo completo dentro de 28 m. A raiz continua
  interpolando posição horizontal e orientação por quadro. Teleportes e
  edições do solo antecipam a pose. Jogador e NPCs conversáveis seguem
  com pose completa. [Arquitetura](../../ARCHITECTURE.md)

## Validação e entrega

- As 129 verificações de `RulesSmoke` passaram. O worker WASM e o cliente
  Blazor foram compilados em Release.
- Movimento, colisões, diálogos e coleta passaram localmente em ambos os
  modos. O teste de cena em produção verificou interpolação, movimento dos
  braços, o rótulo após teleporte e a remoção do avatar no worker e na
  thread principal. O SHA `4a39081d9349a1675b56e08b8e30c0885cb09f80`
  foi conferido no manifesto e no HTML servido. [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)

### Comparativo A/B

Chromium com WebGL no worker, resolução 640 × 360 e a mesma cena para as
duas versões. Metade dos avatares ficou perto do jogador. A outra metade
ficou a mais de 32 m. Em movimento, o destino mudou a cada 12 quadros.
O harness esperou os assets e aqueceu 90 quadros antes do primeiro cenário
e 36 após preencher cada cenário, seguido de 90 amostras. O renderer foi
identificado como SwiftShader ou AMD integrado (radeonsi).

Todos os tempos da tabela são p95 em milissegundos, antes → depois. A
coluna WASM inclui a chamada da patrulha, as chamadas dos avatares e a
transferência do resultado. O tempo do adaptador é a diferença amostrada
entre a atualização dos personagens e o WASM instrumentado. A coluna
laço mede o trabalho síncrono desde o início do callback, incluindo
`scene.render()`. O intervalo é o tempo entre os fins de dois quadros e
inclui o custo de apresentação e agendamento do navegador.

| Renderer | Avatares | Estado | Atualização | WASM | Adaptador | Laço | Intervalo |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| SwiftShader | 12 | Repouso | 0,4 → 0,3 | 0,4 → 0,3 | 0,1 → 0,1 | 7,1 → 6,7 | 66,3 → 67,0 |
| SwiftShader | 12 | Movimento | 0,2 → 0,2 | 0,2 → 0,2 | 0,1 → 0,1 | 6,3 → 6,8 | 67,0 → 67,4 |
| SwiftShader | 32 | Repouso | 0,9 → 0,3 | 0,8 → 0,2 | 0,2 → 0,1 | 11,0 → 11,7 | 96,2 → 96,0 |
| SwiftShader | 32 | Movimento | 0,3 → 0,3 | 0,3 → 0,2 | 0,2 → 0,1 | 10,6 → 10,7 | 98,1 → 92,5 |
| SwiftShader | 64 | Repouso | 0,6 → 0,3 | 0,5 → 0,2 | 0,3 → 0,2 | 19,6 → 17,9 | 136,1 → 138,3 |
| SwiftShader | 64 | Movimento | 0,5 → 0,4 | 0,5 → 0,3 | 0,2 → 0,2 | 17,3 → 17,8 | 137,8 → 136,1 |
| AMD | 12 | Repouso | 0,3 → 0,3 | 0,3 → 0,3 | 0,1 → 0,1 | 6,2 → 5,9 | 18,8 → 19,2 |
| AMD | 12 | Movimento | 0,2 → 0,2 | 0,2 → 0,2 | 0,1 → 0,1 | 5,9 → 6,6 | 18,6 → 18,7 |
| AMD | 32 | Repouso | 0,8 → 0,2 | 0,8 → 0,2 | 0,2 → 0,1 | 8,9 → 10,0 | 20,0 → 21,4 |
| AMD | 32 | Movimento | 0,3 → 0,3 | 0,3 → 0,3 | 0,1 → 0,1 | 9,4 → 7,7 | 20,2 → 19,7 |
| AMD | 64 | Repouso | 0,4 → 0,2 | 0,4 → 0,2 | 0,2 → 0,1 | 15,5 → 11,7 | 20,2 → 20,5 |
| AMD | 64 | Movimento | 0,4 → 0,3 | 0,4 → 0,2 | 0,2 → 0,1 | 13,0 → 12,7 | 20,9 → 19,3 |

- SwiftShader, 12 em movimento. Atualização média de 0,141 → 0,139 ms.
- SwiftShader, 64 em movimento. Atualização média de 0,429 → 0,259 ms.
- AMD, 12 em movimento. Atualização média de 0,138 → 0,104 ms.
- AMD, 64 em movimento. Atualização média de 0,349 → 0,164 ms.

O comparativo mede sessões curtas num único equipamento. A instrumentação
faz leituras do cronômetro por chamada WASM e altera ligeiramente seu custo.
Diferenças pequenas no intervalo entre quadros variaram entre rodadas.
Os resultados são evidência de menor custo do lote, com efeito no FPS
dependente da carga de renderização.

Para repetir, servir dois publishes Release e passar as URLs dos builds
a `node tests/remote-performance.cjs`. O padrão usa SwiftShader. Para
repetir com a GPU desta bancada, definir `ULTIMA_CHROMIUM_ARGS` como
`--enable-gpu --ignore-gpu-blocklist --use-gl=angle --use-angle=gl`.
[Harness de desempenho](https://github.com/sufficit/sufficit-game-ultima/blob/865ca328adf67db9e5d172997ad05e4632eb6a20/tests/remote-performance.cjs)
