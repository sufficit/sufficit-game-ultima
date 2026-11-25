# Migração dos testes de multidão e avatares remotos para C#

## Investigação

O teste de multidão valida níveis de detalhe, descarte e recursos
compartilhados. A bancada de avatares mede o tempo de atualização, o custo
das chamadas WASM e os intervalos de quadro entre duas publicações.

## Alterações

- Adicionados os cenários `crowd` e `remote-performance` ao
  [runner Microsoft.Playwright](../../tests/WorldCollisionE2E).
- A bancada prepara as posições em C# e envia um lote à sonda. A captura
  permanece no worker para medir o quadro sem uma travessia por amostra.
  C# verifica as amostras e calcula média e p95.
- O runner aceita as duas URLs da bancada. SwiftShader é o padrão, e
  `ULTIMA_CHROMIUM_ARGS` permite fornecer os argumentos para GPU física.
  Os comandos estão na [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração).

## Validação e entrega

- Legado e C# passaram com 100 e 300 avatares em worker e main no
  [site publicado](https://sufficit.github.io/sufficit-game-ultima/) no build
  `865ca328adf67db9e5d172997ad05e4632eb6a20`. Ambos confirmaram LOD de 3.226
  para 876 vértices e descarte integral ao sair do alcance.
- A bancada percorreu o mesmo build duas vezes por motor gráfico, com 12,
  32 e 64 avatares, parados e em movimento. Cada caso registrou 90 quadros,
  com chamadas WASM instrumentadas. Os tempos foram observados para
  validar a bancada, sem usar duas revisões de jogo para medir uma mudança.
- O restore com lock file, o build Release da suíte, o RulesSmoke,
  o publish Release e a validação da sintaxe da sonda passaram.
