# Migração dos testes de mobile e de registro de conversas para C#

## Investigação

Quatro arquivos em `tests/` ainda rodavam em node: `ui-mobile.e2e.js` (layout e
controles de toque), `dialog-log.e2e.js` (fila de conversas) e o observador
`game-command-conflicts.cjs` com o teste dele. Todos importam `playwright-core`,
que o repositório não declara (não há `package.json`): só rodavam com `NODE_PATH`
apontando para uma instalação fora do projeto. Os demais testes de navegador já
estavam na [suíte em C#](../../tests/WorldCollisionE2E).

Linha de base do legado, com um publish Release local servido em HTTP e o backend
Release em porta local:

- `ui-mobile.e2e.js`: 36 verificações aprovadas em 200 s.
- `dialog-log.e2e.js`: 7 aprovadas e 3 falhas, nenhuma na fila de conversas:
  - `[motor] falha no boot: terrain.WeatherProfile is not a function`: o cache
    local de `terrain-wasm` era anterior ao commit `edd3ae6`. O site publicado
    serve o módulo atual; depois de `scripts/build-terrain.sh` o motor sobe.
  - O teste conta como erro todo `Failed to load resource` de 409 sem conferir a
    resposta, e a tolerância por URL não conhece o 503 `ai_unavailable` de
    `/v1/game/garden/dialog` (rota de 24/11) nem o 401 do `leave` disparado pelo
    `pagehide`. O teste tinha parado de acompanhar o produto.

## Alterações

- [tests/WorldCollisionE2E](../../tests/WorldCollisionE2E):
  - `MobileLayout.cs` (cenário `mobile-layout`): retrato 390×844, paisagem
    844×390, desktop 1280×720 e tablet 768×1024, com toque emulado por
    `PointerEvent`. Mede canvas, rolagem, sobreposições e alvos do HUD, arrasta os
    sticks, abre e fecha o diálogo pelos botões A/B, simula o teclado virtual
    (390×500), abre o menu pelo fab e confere WASD, mouse e E/Esc no desktop contra
    a linha de base de 22/11 (`decisions/ui-mobile-baseline.json`, copiada para a
    saída pelo projeto).
  - `DialogLogLive.cs` (cenário `dialog-log`): contra o backend Release real em
    `ULTIMA_API`, como `world-undo`. Abre a conversa com a Guardiã, espera a pose
    do teleporte chegar ao servidor, confere o turno na fila, o `flush()` com
    confirmação por ids, a listagem no servidor e o envio pelo `pagehide`. Cada
    resposta a partir de 400 e cada erro de console precisa de um motivo conhecido
    (reentrada de sessão, saída da sessão, servidor sem IA, conflito de pose
    comprovado pela resposta) ou vira falha.
  - `CommandConflicts.cs`: a regra do 409 da rota de comandos (só `pose` com
    `too_fast` tolera a mensagem do console, uma por resposta) passou a viver
    aqui; `SelfCheck` avalia os quatro casos do antigo teste em node antes de
    abrir o navegador.
- Os quatro arquivos em node saíram do repositório. Os registros
  `decisions/ui-mobile-baseline.json` e `decisions/ui-mobile-after.json` ficam.
- Esta etapa não altera `src/` nem `app/`.

## Validação e entrega

- Build da suíte: 0 erros, 0 avisos.
- Site publicado (build `2a6c089`), Chromium com SwiftShader, rodada final com o
  código que foi publicado (compilação, backend e site locais, os três cenários em
  sequência, 186 s no total):
  - `mobile-layout`: aprovado em 76 s (66–76 s nas rodadas anteriores), contra
    200 s do legado, que usava esperas fixas.
  - `dialog-log`: aprovado no worker (25 s) e na thread principal (45 s), com o
    backend local encaminhado pela origem do site. Sem IA configurada, a Guardiã
    responde com o aviso de indisponibilidade e o turno é registrado do mesmo
    jeito.
- Durante a validação, o cenário de conversas no worker falhou duas vezes por
  corrida do próprio ensaio, não do jogo: `cameraState()` no worker devolve uma
  Promise, e o teleporte é local, de modo que perguntar à Guardiã antes de a pose
  chegar ao servidor gera `409 guardian_out_of_reach`. O cenário agora espera a
  posição que o servidor registra, em vez de um tempo fixo.
