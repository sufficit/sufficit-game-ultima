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
- Nota de instrumento: leituras únicas agrupadas num único `evaluate` multicall
  apresentaram valores defasados de vez em quando (worker e main); as séries
  temporais e os fluxos reais (diálogos, OPFS, contagens) são consistentes entre
  si — o fantasma era do harness, não do jogo.
- `dotnet publish -c Release` sem erros; deploy do Pages concluído (push
  `07ff4f2`); produção https://sufficit.github.io/sufficit-game-ultima/:
  - modo worker (padrão): série temporal — **6/6 modelos, 6/6 GLB, 450/450
    instâncias em ~4 s**; diálogo da Guardiã com resposta e origem; Andarilho;
    OPFS 12 acertos na 2ª visita; chip "GPU: … · render em worker";
  - fallback `?render=main`: 6/6 modelos, 450 instâncias GLB, diálogo completo,
    sem marca de worker.
- IA da Guardiã (WebGPU/`ai.js` e CPU/`ai-cpu.js`, main thread) inalterada e
  coordenada pelos mesmos eventos; validada via diálogo no worker.
