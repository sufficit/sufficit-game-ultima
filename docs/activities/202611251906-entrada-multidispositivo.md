# Entrada de teclado, toque e gamepad

## Investigação

O worker e a thread principal capturavam os mesmos dispositivos em pontos
diferentes. O teclado mantinha estado no JavaScript e os analógicos virtuais
tinham uma zona morta independente da do controle. O worker recebia a
amostra do gamepad a cada quadro, mesmo com os eixos em repouso.

## Alterações

- [Ponte do navegador](../../src/wwwroot/js/game.js) compartilhada nos dois
  modos. A captura descarta leituras repetidas de eixos, dispara interações
  na borda de pressão, limpa entradas ao perder foco e preserva o primeiro
  dedo no analógico virtual até soltura ou cancelamento.
- [PlayerInput](../../engine/Ultima.Terrain/PlayerInput.cs) resolve WASD,
  setas, corrida e os eixos de deslocamento e câmera. Toque e gamepad usam a
  mesma zona morta radial. O motor só lê um novo lote quando o estado muda.
- O [guia do jogador](../USAGE-JOGO.md#controles) descreve o direcional e
  o efeito da troca de foco. A [arquitetura](../../ARCHITECTURE.md) registra
  o fluxo de entrada.

## Validação e entrega

- O publish do terreno e do jogo em Release, o RulesSmoke e a compilação
  do E2E passaram. Em Chromium com SwiftShader, `camera-streaming` passou
  em worker e `mobile-layout` passou na thread principal.
- [Cenário input-devices](../../tests/WorldCollisionE2E/InputDevices.cs)
  passou em worker e na thread principal, localmente e no GitHub Pages.
  Cobriu teclas equivalentes, perda de foco, segundo dedo, cancelamento de
  toque, zona morta, desconexão e interação por pressão única.
- Em 12 leituras do Gamepad API com o controle simulado em repouso, o build
  anterior (`6e68238`) enviou 12 mensagens de eixos ao worker. O novo build
  enviou zero, tanto em repouso quanto com um eixo mantido por 12 leituras.
  O teste usou eventos de toque e gamepad simulados no navegador.
- A revisão de código `dded6c13aed173abe99b29352eee5d1a48aac78b` foi
  confirmada no `build.json` e na página pública.
  [Jogo](https://sufficit.github.io/sufficit-game-ultima/)
  [Execução no GitHub Actions](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45709881416)
