# Decision — UI mobile do Ultima (t56)

Data: 2026-11-22 · Escopo: jogável no celular (retrato e paisagem), menus ocultos atrás de um botão, alvos de toque no tamanho certo; desktop sem regressões. Autorizado por voz em 22/11.

## Premissas
- Desktop continua principal: media queries só mudam comportamento em `max-width`/`pointer: coarse`; 1280×720 mantém geometria idêntica.
- Ponto de corte mobile: `max-width: 760px` OU `pointer: coarse` (mesmo critério já usado para mostrar .touch-controls). Tablet 768×1024 é smoke test (fica acima de 760px → comportamento desktop, sem quebrar).
- IDs lidos pelo JS permanecem únicos e sempre renderizados: game-canvas, move-stick, move-thumb, look-stick, look-thumb, touch-action, touch-cancel, dialog-input, network-chat-input, ultima-dialog-queue-count, workshop-prompt, workshop-scale.
- Botões da SUI dentro de painéis já têm tamanho de componente; o critério 48×48 será medido e, onde faltar, minimum-height CSS por classe (sem tocar no pacote).
- Publicação pelo caminho github_cli api (git/trees → git/commits → PATCH refs/heads/main), push direto não funciona. Só publicar quando origin/main já contiver o commit t50 (mensagem "t50"/"dialog").

## Estrutura escolhida
- `.ultima-menu` envolvendo status + oficina + toggle + painel: `display: contents` no desktop (posições idênticas às atuais); vira folha quase inteira (retrato) / gaveta rolável (paisagem) no mobile, aberta pelo botão hamburger. Sem duplicar IDs.
- Altura: 100dvh com fallback 100vh; `viewport-fit=cover` + `env(safe-area-inset-*)`; sem maximum-scale (zoom acessível preservado); font-size ≥16px no campo de diálogo (evita auto-zoom iOS).
- Uma única fonte da verdade para toque: bloco `.touch-*` sai do extras.css e fica só no app.css (extras.css mantém apenas o que é identidade).
- Diálogo no mobile: folha inferior com safe-area; sticks ocultos enquanto diálogo aberto (A/B continuam); game.js recalcula raio por pointerdown via getBoundingClientRect (verificado — raio não fica em cache).

## Medidas (linha de base — antes)
A MEDIR após build local (ports: 8090 API em uso, 8091 site em uso → usar 8092/8093):
- Retrato 390×844, paisagem 844×390, desktop 1280×720: retângulos de .ultima-status, .workshop-panel, .ultima-panel-toggle, .ultima-panel, .ultima-chat-log, .ultima-dialog, #move-stick, #look-stick, #touch-action, #touch-cancel, canvas.
- JSON completo será gravado em decisions/ (ui-mobile-baseline.json) e resumido aqui.

## Decisões tomadas
- game.js: resize só escuta window.resize; adicionar `visualViewport.resize` + `orientationchange` redundando o deliver (barato, cobre iOS).
- Em paisagem baixa (≤480px de altura): controles encolhem até mínimo (sticks 112, A 64, B 52 — valores mínimos do enunciado) e ficam ancorados nos cantos; nenhum HUD no centro.

## Progresso
- [x] 22:20 — Árvore conferida: HEAD local 8b19ea3; origin/main 7a9e45d **já contém t50** ("t50: registro de conversas...") → via de publicação liberada quando tudo verde.
- [x] 22:22 — Leitura: index.html, app.css, extras.css, Index.razor (HUD/touch/diálogo), game.js (sticks recalculam raio por evento — sem cache; resize só window), harness tests/dialog-log.e2e.js (padrão playwright-core + chromium headless shell 1255).
- [~] 22:23 — dotnet publish Release → /tmp/ultima-mobile rodando em segundo plano (log /tmp/ultima-t56/publish-baseline.log).
- [ ] Medir linha de base nos 3 viewports + screenshots "antes".
- [ ] Implementar CSS/Razor/JS.
- [ ] tests/ui-mobile.e2e.js + medições "depois".
- [ ] Build Release + dialog-log.e2e.js.
- [ ] Commit, publicar main, Pages success, validação ao vivo.

## Concorrência
- Sub-sessão t50 ativa: não tocar em dialog-log.js, tests/dialog-log.e2e.js, app/; regra read-before-write em Index.razor/app.css/extras.css/index.html/game.js; nunca git checkout/restore/reset/stash/clean.
- Memória cgroup 2 GB: um build e um navegador por vez; conferir ps antes de cada um.
