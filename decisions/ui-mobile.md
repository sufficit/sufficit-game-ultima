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
- [~] Build Release pós-merge OK (publish-after2.log, `-m:1`: saída /tmp/ultima-mobile2/wwwroot — site em .../wwwroot/wwwroot, conteúdo conferido: playerState ✓, ultima-menu ✓ (21), viewport-fit=cover ✓). Servido em 127.0.0.1:8092; **validação completa (ui-mobile.e2e.js modo full) em execução** — log /tmp/ultima-t56/after-run.log, relatório decisions/ui-mobile-after.json.
- [~] 1ª validação pós-edição (after-run.log): **canvas == viewport ✓ nos 4 viewports (390×844, 844×390, 768×1024 — cadeia 2:1 resolvida)**, sem rolagem ✓, cena ativa ✓ (cameraState/playerState respondem no modo worker), menus ocultos ✓ (só fab). Falhas: (a) move-stick com 87,6px no retrato — flex shrink no container (corrigido: `flex:none` + A/B empilhados no retrato ≤480px); (b) falso positivo "sobreposição" polegar∩próprio stick (corrigido no teste: pares pai→filho não contam). 2ª validação (after-run2.log, CSS novo servido): **sticks 124×124 ✓, A/B ≥ mínimos ✓, smallTargets=[] no retrato e paisagem** — falhas restantes eram do TESTE, não do jogo: (a) contador de sobreposição indexava a lista não-filtrada (polegar∩próprio stick contava; corrigido p/ `rects[i].n`); (b) captura do tablet travava 30s (try/catch, timeout 15s). Pendências observadas: desktop cameraState n/d nessa rodada; smallTargets desktop só em elementos do chat (input 286×36 — critério 48px é mobile; escopo a revisar). 3ª validação (after-run3.log) rodando com o teste corrigido.
- [~] Run 4 completo (after-run4.log, 00:06-00:18): **retrato/paisagem/tablet 100% ok — canvas==viewport, sem rolagem, cena ativa, sem sobreposição, menus ocultos (só fab), sticks 124×124, A 68/B 56, smallTargets=[], rotação retrato→paisagem acompanha**. As 9 falhas são do HARNESS, não do jogo: (1) arrasto do stick despachava down→up na mesma task — o core nunca amostrava o vetor; (2) esperas de 8-15 s insuficientes para reações do Blazor a 1-2 fps headless (fab demora ~10 s para abrir, WASD andou 0,33 m em 2,2 s); (3) 404 de assets/assets.json porque o publish local não roda scripts/build-assets.sh (catalog montado à mão no 8092 para validação). Teste corrigido: espera cameraState, stickDown/stickUp com amostragem por 20 s, timeouts 20-30 s.
- [~] Run 7 v4 (12:36–12:43, harness COMPLETO após blindagem do boot): retrato/paisagem/tablet 100% (canvas==viewport, sem rolagem, cena ativa, sticks/A/B nos mínimos); diálogo abre por A, texto legível, teclado virtual não empurra a folha pra fora. 4 falhas, cada uma diagnosticada por sonda dedicada (probe-failures.js, contexto idêntico ao harness):
  1. **botão-ícone ↻ 27×54px** na folha do menu → CORRIGIDO (`min-width:48px` p/ botões da folha, app.css).
  2. **404 assets/assets.json** no console (pipeline GLB do t50 espera catálogo assinado; pasta inexistente no repo) → CORRIGIDO com catálogo vazio `src/wwwroot/assets/assets.json` (o jogo segue só com modelos DLL, por design).
  3. **B fecha diálogo** e 4. **menu fecha por close/backdrop** → FLAKY, não defeito: sonda confirma listeners rodando (pointerdown ×1) e o fechamento acontecendo; é timing do render Blazor sob swiftshader (~1-2 FPS) estourando a espera. Run 8 re-testa; se persistir, a espera é do harness, não do produto.
  - Nota de build: o publish atual grava em `/tmp/ultima-mobile2/wwwroot` (layout mudou do histórico `.../wwwroot/wwwroot/wwwroot`); o script do run 8 confere a raiz certa e religa o servidor nela.
- [~] Regressão desktop (run 7 v4): `.ultima-status` e `.ultima-panel-toggle` Δh=0 vs linha de base; sticks ocultos no desktop; WASD anda (0,0 → 0,1.63). Mouse e E/Esc ficam para o dialog-log (t5).
- [~] Run 8 (13:03–13:11) EXIT=1: **layout/HUD 100% ok** (↻ ≥48, assets.json sem 404, 409 não reapareceu); restam exatamente 3 falhas determinísticas de fechamento (retrato B, menu por close/backdrop, desktop Esc) — todas com `waitForSelector state:hidden` de 90 s expirando integralmente.
- [~] **Veredito da probe4 (13:53–13:57, fluxo FIEL do harness, mesma raiz 8092)**: **os 3 fechamentos FUNCIONAM no produto** — B fechou (pointerdown no doc 0→1, nó do B não trocado, listener original do boot), menu fechou por close/backdrop, desktop Esc fechou com `activeElement=game-canvas` (gate de editável descartado). Único erro de console: **409 `/v1/game/command`** — a URL antes anônima, agora identificada: é o servidor de rede do jogo (`w-…run.sufficit.com.br`), esperado em rede compartilhada/sem sessão; **não é defeito do HUD mobile** (fora do escopo t56, a registrar como limitação).
- [~] **Run 9** (job 086a-7e07, harness corrigido): as 3 esperas `state:hidden` trocadas por **polling direto de `getBoundingClientRect().height===0` + até 4 toques/teclas com 15 s cada** — a via que a probe4 comprovou fechar. Log em /tmp/ultima-t56/run9-test.log; critério 0 FALHOU.
- [ ] Regressão desktop completa (rects == linha de base, WASD, de preferência mouse, E/Esc, toggle painel) — pendente do dialog-log no t5.
- [ ] dotnet build Release limpo + dialog-log.e2e.js passando.
- [ ] Commit final + publicação main via github_cli api — **BLOQUEADO: t50 (5807ebc) AINDA não está na origin/main** (verificado 23:35 UTC; HEAD da main = 9e60ff5). Enquanto isso: validar localmente e esperar, sem push.

## Concorrência
- Sub-sessão t50: arquivos dela (dialog-log.js, tests/dialog-log.e2e.js, app/) não alterados; regra read-before-write respeitada; nenhum checkout/restore/reset/stash/clean.
- Memória cgroup 2 GB (1,36 GB em uso às 23:20): um build e um navegador por vez; servidores antigos de /tmp/ultima-mobile* derrubados antes de servir o novo.
