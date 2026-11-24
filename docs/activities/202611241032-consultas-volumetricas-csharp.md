# Consultas volumétricas da câmera e do piso em C# WebAssembly

## Investigação

A câmera consultava o WASM para cada amostra de proximidade e de colisão com
volumes. A busca vertical do piso recalculava a altura da superfície a cada
sonda da mesma coluna. O teto de uma escavação consultava 24 pontos pelo
JavaScript.

## Alterações

- A câmera recebe as amostras volumétricas em lote e combina os resultados
  com a superfície triangulada e as malhas de objetos na ordem do raio.
- A busca do piso lê a altura da superfície uma vez por coluna e reutiliza
  esse valor durante a varredura e o refinamento.
- O C# verifica o contorno para decidir se uma escavação pode receber teto.
  O Babylon.js cria e posiciona a malha do teto.

## Validação e entrega

- Builds Release do worker e do cliente Blazor. As 116 verificações de
  `RulesSmoke` passaram.
- Em Chromium com SwiftShader, passaram túneis, emendas, câmera com streaming
  e coleta em worker e na thread principal.
- Microbenchmark no worker, usando quatro sondas fixas em um túnel curvo e
  sete lotes de 4.000 chamadas por build. A mediana caiu de 34,4 para 11,1 ms
  por lote. A soma das alturas retornadas foi idêntica. A medida cobre
  a busca do piso nesse cenário.
  [Arquitetura](../../ARCHITECTURE.md)
