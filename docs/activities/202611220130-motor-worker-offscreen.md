# Motor em Web Worker com OffscreenCanvas (fallback main thread intacto)

## Investigação

O render e o loop vivam na main thread junto com a UI Blazor, a rede P2P e a voz —
qualquer pico de CPU do motor travava tudo. A referência é o lineage-rust (engine
inteira num worker dedicado, input capturado no DOM e postado tipado —
`web/main.js`, `web/worker.js`). O protótipo (branch `worker-proto`, commit
`d3eed90`) comprovou: Babylon sobe no worker via `importScripts` do mesmo CDN,
**DynamicTexture funciona via OffscreenCanvas 2D**, FPS idêntico (60 estável),
custo do bundle duplicado aceitável (~316 ms únicos de parse no contexto do
worker, cache HTTP do CDN). Restrições descobertas na janela de execução: a frente
de assets GLB injeta o loader glTF por `<script>` com SRI (inexistente em worker) e
os caminhos relativos de `fetch` no worker resolvem contra `/js/` (URL do script).

## Alterações

Commit `07ff4f2` (5 arquivos, +1976/−1508):

- `js/game-core.js` — todo o motor extraído do game.js como
  `UltimaCore.create(host)`, sem DOM: `host = { canvas, mode, build, pageBase,
  global, dom, postEvent, sendPose, onInputMode, onCoreReady, onCoreError }`.
  Eventos `.NET` saem por `postEvent` (interface `dotnet` preservada nos pontos de
  chamada); pose da rede por `sendPose`; boot async aquece o OPFS antes da cena e
  só então notifica `onCoreReady`; `pageUrl()` resolve fetch do catálogo/GLB
  relativos à PÁGINA; `makeGrassTexture` com `OffscreenCanvas`; loader glTF
  bifurcado — main: `<script>` com SRI (como antes); worker: download + SHA-384
  via `crypto.subtle` + `importScripts` de blob URL, a MESMA garantia de
  integridade; câmera manual por deltas (`camPending`) quando sem `attachControl`.
- `js/game.js` (shell) — decide o modo: worker quando há
  `transferControlToOffscreen` (a menos de `?render=main`); fallback direto com
  Babylon injetado com o mesmo SRI (saiu da tag do index.html — no modo worker a
  main thread não baixa nem parseia a engine); worker que falha no boot recarrega
  em modo main uma vez por sessão (`sessionStorage`); ponte de mensagens
  (eventos→JSInterop, pose→net/live, input→worker, comandos fire-and-forget,
  diagnósticos `status/report/get` por request id); `OnEngineReady` ganha o sufixo
  `· render em worker` no HUD (zero mudança em C#).
- `js/game-worker.js` — `importScripts` do Babylon (CDN), `assets.js` e
  `game-core.js`; init recebe `{canvas, build, pageBase}`; ready só quando o core
  avisa cena pronta; erro de boot vira `fatal` (shell cai no fallback).
- `js/assets.js` — `init(build)` aceita o build por parâmetro (worker não tem
  `<meta>`); global `self` no worker.
- `src/wwwroot/index.html` — tags reordenadas (`game-core.js` antes do shell;
  Babylon sai da tag).
- Correções de migração descobertas em teste: `Object.assign` não preserva
  getters (`game.mode` congelava — `defineProperty`); registers chegavam antes da
  cena ("cena ainda não criada" ×3 — `onCoreReady` passou a segurar o
  `game.init`); dois `assets.js` (main e worker) — relatório do motor exposto por
  `assetsReport` do próprio contexto.

## Validação e entrega

- Local (build publicado, Chromium headless/SwiftShader, suíte `worker-check`):
  **13 checks OK** — modo worker ativo com HUD marcado; 6/6 modelos DLL; 6/6
  assets GLB com espalhamento; diálogo da Guardiã com resposta e origem
  ("servidor · regras"); diálogo do Andarilho; OPFS na 2ª visita com 12 acertos
  (5 texturas + terreno + 6 GLB); fallback `?render=main` sem marca de worker,
  6/6 modelos (série temporal monotônica `0→4→5→6`) e diálogo completo.
- Nota de instrumento (corrigida em 22/11): esta validação leu, de vez em quando,
  contagens incompletas (4 ou 5 modelos de 6, "0 instâncias"). A causa, descoberta
  depois: nesta versão do playwright-core (1.64.1), `waitForFunction` com predicado
  async **não espera** — resolve assim que a Promise resolve, mesmo devolvendo
  `false`. As esperas desta suíte nunca esperaram o jogo carregar, e as leituras
  logo a seguir pegavam a página ainda carregando. Não era do jogo. Revalidado com
  espera real em 22/11 (build `39f48a5`, produção, ver
  [202611222015-layout-altura-casca.md](202611222015-layout-altura-casca.md)):
  6/6 modelos e ~450 instâncias confirmados antes de cada medição, diálogo da
  Guardiã com resposta e origem, câmera por arrasto e cache local na 2ª visita
  (15 acertos, nada regravado). O tamanho do buffer e a nitidez não dependiam da
  espera: foram lidos segundos após a carga, com o buffer já definido.
- `dotnet publish -c Release` sem erros; deploy do Pages concluído (push
  `07ff4f2`); produção https://sufficit.github.io/sufficit-game-ultima/:
  - modo worker (padrão): série temporal — **6/6 modelos, 6/6 GLB, 450/450
    instâncias em ~4 s**; diálogo da Guardiã com resposta e origem; Andarilho;
    OPFS 12 acertos na 2ª visita; chip "GPU: … · render em worker";
  - fallback `?render=main`: 6/6 modelos, 450 instâncias GLB, diálogo completo,
    sem marca de worker.
- IA da Guardiã (WebGPU/`ai.js` e CPU/`ai-cpu.js`, main thread) inalterada e
  coordenada pelos mesmos eventos; validada via diálogo no worker.

## Correção de regressão (22/11/2026): render em 300×150 esticado pela tela

**Relato do mantenedor:** a qualidade do jogo "piorou muito" — cena borrada, bordas
serrilhadas e rótulos de nome ilegíveis.

**Causa** (regressão desta frente, commit `07ff4f2`): o OffscreenCanvas transferido
nasce com o tamanho padrão do elemento (300×150) e, dentro do worker, não tem layout
— nem o Babylon nem `engine.resize()` têm como descobrir o tamanho de exibição. O
shell mandava largura/altura na mensagem de init, mas com o valor errado
(`canvas.width`, o intrínseco) e ninguém os aplicava. Medido na produção: buffer de
**300×150** exibido em 1280×640 CSS (na tela wide/retina do mantenedor, 8–17× de
esticamento); o fallback renderizava em 1280×640.

**Por que a validação original não pegou:** os testes conferiram DOM e dados
(modelos, diálogo, OPFS), não os pixels — e o `perf` até parecia bom, porque
renderiza-se em 300×150.

**Correção** (commit `208ff8a`): o shell mede o canvas (CSS × `devicePixelRatio`) e
envia no init e a cada mudança (`ResizeObserver`, `resize`, `matchMedia` para zoom e
troca de monitor); o worker trata a mensagem `size`; o core cria o engine com
`adaptToDeviceRatio` só na main e, no worker, aplica
`setHardwareScalingLevel(1/dpr)` + `setSize` em pixels. Semântica conferida no
código-fonte do Babylon 9.36: `setSize` grava o buffer direto; o fator de escala só
converte CSS↔pixels dentro de `resize()` (a primeira tentativa passava o tamanho
CSS ao `setSize` e deixava a tela 2× com metade da resolução). Diagnósticos novos:
`game.renderSize()` e `game.cameraState()`.

**Validação por pixels** (Chromium headless, cena completa antes da foto):

| | antes (produção) | depois (produção) | fallback |
|---|---|---|---|
| buffer, tela 1× | 300×150 | 1280×640 | 1280×640 |
| buffer, tela 2× | 300×150 | 2560×1280 | 2560×1280 |
| detalhe no miolo do canvas, 1× | 35 · 29 · 10 | 834 · 224 · 76 | 1139 · 457 · 86 |
| detalhe no miolo do canvas, 2× | 8 · 7 · 2 | 478 · 238 · 65 | 533 · 342 · 109 |
| razão de autorredução, 1× | 2,30 | 31,53 | 29,80 |
| razão de autorredução, 2× | 2,21 | 23,12 | 22,78 |

- "Detalhe" = variância do Laplaciano em três faixas centrais (sem HUD). A cena é
  aleatória a cada carga, então esses valores variam com o conteúdo.
- **Razão de autorredução** = detalhe da captura ÷ detalhe da mesma captura
  reduzida à metade e reampliada. É a medida que não depende de conteúdo: imagem
  nativa perde muito nessa operação (razão alta); imagem já esticada quase não
  muda (razão perto de 1). Calibração: fallback reduzido 4× e esticado → 1,29.
  Todas as capturas de fallback ficaram em 21,7–29,8; todas as do worker
  corrigido em 23,1–33,0; o defeito em 2,2–2,3.
- Composição do canvas transferido conferida isoladamente (teste sem Babylon):
  linhas de 1 px chegam com 1,00 px em dpr 1 e 2.
- Redimensionamento: 1280×640 → 900×450 (1×) e 1800×900 (2×). Câmera no worker
  por arrasto e roda responde (Δα −0,8; Δβ −0,32; Δraio −0,075).
- Inventário visual às cegas da página inteira: mesmos itens presentes/ausentes
  nos dois modos; luminância e saturação na mesma faixa.
- Ressalva: um avaliador visual classificou um recorte ampliado da produção
  corrigida como "ampliado" — era ruído de julgamento sobre um personagem de
  superfícies lisas em close; a razão de autorredução e o buffer são a evidência.

**Lição:** mudança em render/canvas se valida nos pixels (tamanho do buffer e
razão de autorredução), não só em DOM e dados.
