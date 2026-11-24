# Movimento do Andarilho e de avatares remotos em C# WebAssembly

## Investigação

O Babylon.js calculava o deslocamento circular do Andarilho e a interpolação
dos avatares remotos. Na sequência, pedia ao C# a pose de cada ator. O tempo
de quadro já era passado para o cálculo da pose.

## Alterações

- Patrulha, interpolação de posição e rotação e distância percorrida são
  calculadas no WASM e alimentam o cálculo da marcha na mesma chamada.
  A resposta para esses atores usa 29 valores. O bloco de 35 valores
  permanece para a pose do jogador e as sondas de apoio dos pés.
- O motor recebe poses da rede, aplica os transforms, atualiza os rótulos
  e suspende a patrulha durante o diálogo do Andarilho.
- A arquitetura registra a responsabilidade de cada camada. O plano
  MobBrain permanece como frente independente.
  [Arquitetura](../../ARCHITECTURE.md)

## Validação e entrega

- Build Release do worker e publish Release do cliente Blazor.
  As 113 verificações em `RulesSmoke` passaram.
- Colisão, movimento e avatares remotos exercitados nos dois modos.
  A patrulha manteve o raio de 1,4 m, parou ao abrir o diálogo do Andarilho
  e retomou ao fechá-lo. O teste de janelas declarativas também passou.
- Medição de CPU do trecho de atualização dos outros personagens com
  Chromium SwiftShader, 12 avatares simulados e 180 quadros por build.
  Mediana de 0,10 ms nos dois builds. Média de 0,132 para 0,135 ms.
  p95 de 0,20 para 0,30 ms. A resolução observada do cronômetro foi 0,1 ms.
- A comparação foi feita com renderização por software.
