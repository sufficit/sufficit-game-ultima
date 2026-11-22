# Decision — UI mobile do Ultima (t56)

Data: 2026-11-22 · Escopo: jogável no celular (retrato e paisagem), menus ocultos atrás de um botão, alvos de toque no tamanho certo; desktop sem regressões. Autorizado por voz em 22/11.

## Premissas
- Desktop continua principal: media queries só mudam comportamento em `max-width`/`pointer: coarse`; 1280×720 mantém geometria idêntica.
- Ponto de corte mobile: `max-width: 760px` OU `pointer: coarse` (mesmo critério já usado para mostrar .touch-controls). Tablet 768×1024 é smoke test (fica acima de 760px → comportamento desktop, sem quebrar).
- IDs lidos pelo JS permanecem únicos e sempre renderizados: game-canvas, move-stick, move-thumb, look-stick, look-thumb, touch-action, touch-cancel, dialog-input, network-chat-input, ultima-dialog-queue-count, workshop-prompt, workshop-scale.
- Botões da SUI dentro de painéis já têm tamanho de componente; o critério 48×48 será medido e, onde faltar, minimum-height CSS por classe (sem tocar no pacote).
- Publicação pelo caminho github_cli api (git/trees → git/commits → PATCH refs/heads/main), push direto não funciona. Condição t50 na main: **cumprida** (verificado 23:10 — tests/dialog-log.e2e.js e js/dialog-log.js presentes em origin/main 5208961).

## Estrutura escolhida
- `.ultima-menu` envolvendo status + oficina + toggle + painel: `display: contents` no desktop (posições idênticas às atuais); vira folha quase inteira (retrato) / gaveta rolável (paisagem) no mobile, aberta pelo botão hamburger `#ultima-menu-fab` + backdrop `#ultima-menu-backdrop`. Sem duplicar IDs.
- Altura: 100dvh com fallback 100vh **na cadeia completa** (`#app, #app > .sui-root, #app > :has(> .game-shell), .game-shell` — seletor da origin/main 5208961 unificado com dvh do t56); `viewport-fit=cover` + `env(safe-area-inset-*)`; sem maximum-scale; font-size ≥16px no campo de diálogo.
- Uma única fonte da verdade para toque: bloco `.touch-*` saiu do extras.css e fica só no app.css (extras.css mantém só o reposicionamento do painel de rede).
- Diálogo no mobile: folha inferior com safe-area + `--ultima-vv-bottom` (visualViewport); sticks ocultos enquanto diálogo aberto (A/B continuam); raio do stick recalculado por evento.
- Paisagem baixa (≤480px): controles mínimos nos cantos (sticks 112, A 64, B 52).

## Linha de base medida (decisions/ui-mobile-baseline.json, build pré-t56 às 22:58)
- Canvas **2:1 em todas as janelas** (390×195, 844×422, 1280×640, 768×384) — **causa identificada como bug já corrigido na origin/main** (cadeia de altura do host SUI, commits 208ff8a+5208961), não como defeito do t56. Correção integrada por merge local.
- HUD visível no "antes": status, oficina, toggle (+chat em ≥844px de largura) sobrepondo o jogo; 12 alvos <48px (oficina/diálogo); retrato com 13 sobreposições de HUD; paisagem com scroll horizontal.
- meshes=0 era artefato de medição (window.game não expõe `scene`); teste corrigido para usar `cameraState`/`playerState` (novas APIs públicas do core, responder via query nos dois modos).

## Progresso
- [x] Leitura completa (index.html, app.css, extras.css, Index.razor, game.js/game-core.js/game-worker.js, harness dialog-log.e2e.js).
- [x] Linha de base medida nos 4 viewports + JSON + screenshots "antes" (/tmp/mobile-shots/antes-*.png).
- [x] Implementação: viewport-fit=cover (index.html); cadeia de altura + dvh; menu mobile completo no Razor (fab/backdrop/folha/`ToggleMenu`/`_menuOpen`, desktop `display:contents`); app.css com bloco mobile único (typo corrigido, sem escapes, status/oficina/painel/chat ocultos no mobile); extras.css dedup; game.js com visualViewport+orientationchange; diálogo em folha inferior ≥16px.
- [x] Merge da origin/main (5208961, correção do canvas 2:1) → conflitos app.css/game.js resolvidos por união, commit 44dc913 sobre WIP b3ce871. WIP commitado antes do merge para não perder edições.
- [~] Build Release pós-merge → /tmp/ultima-mobile2 (game.js/game-core.js atualizados pós-publish), servido em 127.0.0.1:8093; **validação completa (ui-mobile.e2e.js modo full) em execução** — log /tmp/ultima-t56/after-run.log, relatório decisions/ui-mobile-after.json.
- [ ] Remeção pós-edição verde nos 4 viewports (rotação, sticks, A/B diálogo, menu, teclado virtual).
- [ ] Regressão desktop (rects == linha de base, WASD, mouse, E/Esc, toggle painel).
- [ ] dotnet build Release limpo + dialog-log.e2e.js passando.
- [ ] Commit final + publicação main via github_cli api (t50 já na main ✓) + Pages success + validação ao vivo.

## Concorrência
- Sub-sessão t50: arquivos dela (dialog-log.js, tests/dialog-log.e2e.js, app/) não alterados; regra read-before-write respeitada; nenhum checkout/restore/reset/stash/clean.
- Memória cgroup 2 GB (1,36 GB em uso às 23:20): um build e um navegador por vez; servidores antigos de /tmp/ultima-mobile* derrubados antes de servir o novo.
