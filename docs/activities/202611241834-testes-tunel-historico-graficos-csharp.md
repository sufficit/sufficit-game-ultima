# Migração dos testes de túnel, histórico e gráficos para C#

## Investigação

Os três scripts legados cobriam a malha volumétrica e a câmera dentro do
túnel, a aplicação e reversão de edições em dois clientes e o perfil
gráfico cinema. Os critérios foram comparados no mesmo build publicado.

## Alterações

- `tunnel`, `world-undo` e `graphics` passaram ao
  [runner Microsoft.Playwright](../../tests/WorldCollisionE2E).
- C# controla o navegador, calcula as métricas e verifica os resultados.
  As sondas de posição, raio de seleção e sombras consultam o Babylon.js.
- O histórico usa backend descartável em loopback. Ao testar o cliente
  publicado, o Playwright encaminha as chamadas ao backend de teste.
  Os comandos ficam na [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração)
  e no [guia do mundo](../USAGE-MUNDO.md#validação).

## Validação e entrega

- No build `47c98430d94012f1c8f27239cb5cb855015f58f4` do
  [site publicado](https://sufficit.github.io/sufficit-game-ultima/), as duas
  versões do túnel passaram em worker e main. A malha teve 5.807 vértices.
  Passaram a caminhada externa e interna, a câmera e a restauração da superfície.
- Histórico e undo passaram nas duas versões com dois clientes worker e
  backends isolados. Os clientes convergiram após criar e desfazer um pincel
  de elevação e um corredor curvo.
- As duas versões de gráficos passaram em worker e main com luz ambiente,
  três cascatas, SSAO e profundidade de campo. O avatar distante saiu da
  lista de sombras. O FPS coletado serve como diagnóstico do cenário.
- Passaram o build Release da suíte, restore com lock file, RulesSmoke,
  integração do WorldStore e publish Release do jogo.
