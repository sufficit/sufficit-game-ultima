# Layout: o jogo volta a ocupar a janela inteira (a casca estava sempre em 2:1)

## Investigação

**Relato do mantenedor:** "o display ainda não está correto, perdendo parte da
exibição" — depois da correção da resolução do worker
([202611220130-motor-worker-offscreen.md](202611220130-motor-worker-offscreen.md)).

Navegando no jogo numa janela do tamanho da captura do mantenedor (2305×1078), o
canvas media **2305×1153** — 75 px além da altura da janela, com 13 elementos
(incluindo o HUD da base) fora da tela — em todos os passos (andar, girar a câmera,
zoom). Reduzindo a janela para 1614×1078, o canvas media **1614×807**: 271 px de
faixa sem jogo. Em qualquer tamanho a proporção do canvas dava exatamente 2:1.

A cadeia de altura mostrou o elo quebrado, **igual nos dois modos de render**:
`html`, `body` e `#app` com a altura da janela; `div.sui-root` com altura
automática; `.game-shell` e `.viewport` com o `height: 100%` convertido em
automático (o pai é automático); e o canvas ditando a altura de tudo pela
proporção intrínseca de um canvas sem tamanho (300×150 = 2:1).

**Causa:** o commit `7a9e45d` (nova interface) passou a envolver o jogo no host da
Sufficit.Blazor.UI (`<SUIThemeProvider>` → `div.sui-root`, filho direto de
`#app`). A regra `#app, .game-shell { height: 100% }` não conhece esse elo. Não é
efeito da migração para o worker nem da correção de resolução: o buffer acompanha o
canvas em ambos os casos; o canvas é que tem o tamanho errado.

## Alterações

Commit `5208961`:

- `src/wwwroot/css/app.css` — a cadeia de altura fecha no host:
  `#app, #app > .sui-root, #app > :has(> .game-shell), .game-shell { height: 100% }`.
  O `:has()` cobre o wrapper mesmo se a classe da biblioteca mudar.
- `src/wwwroot/js/game.js` — o canvas passa a ser observado também no caminho sem
  worker (`watchSizeMain`): o Babylon só redimensionava no `resize` da janela, e
  uma mudança de layout sem `resize` (painel, barra de endereço do celular) deixava
  o buffer com o tamanho antigo, deformando a imagem.

## Validação e entrega

Teste ao vivo antes do build (regra injetada no navegador, na produção): canvas =
janela e zero elementos fora em 2305×1078, 1614×1078, 1920×1080 a 150%,
1280×720 a 200% e 900×1600 a 200%.

Build publicado e depois a produção (build `5208961`,
https://sufficit.github.io/sufficit-game-ultima/), nos dois modos, lendo o DOM e o
buffer do motor:

| janela | antes | depois (canvas na tela / buffer) | fora da janela |
|---|---|---|---|
| 2305×1078 | 2305×1153 | 2305×1078 / 2305×1078 | 13 → 0 |
| 1614×1078 | 1614×807 | 1614×1078 / 1614×1078 | 0 → 0 |
| 1920×1080 a 150% | 1920×960 | 1920×1080 / 2400×1350 | 0 |
| 1280×720 a 200% | 1280×640 | 1280×720 / 2560×1440 | 0 |
| 900×1600 a 200% (retrato) | 900×450 | 900×1600 / 1800×3200 | 0 |

- Medido no início, depois de andar, girar a câmera e dar zoom, e depois de mudar o
  tamanho da janela no meio do jogo (o buffer acompanha nos dois modos).
- Capturas da navegação a 2305×1078: tamanho igual ao da janela, imagem do jogo até
  as quatro bordas (0% de pixels iguais ao fundo da página), nitidez nativa (razão
  de autorredução 26–28).
- Elementos do HUD medidos um a um em 2305×1078, 1920×1080 e 1366×768: todos
  inteiros (chat a 18 px da borda inferior, "Painel" a 12 px do topo e da direita).
- Leitura visual independente das capturas: cena nas quatro bordas, sem barras
  vazias; proporções naturais.

**Pendência entregue à frente da Oficina:** em celular deitado com 360 a 412 px de
altura, o bloco "Estilo do mundo" do painel (`section.ws-block`) fica abaixo da
borda inferior. É anterior, só acontece nessa altura e não envolve o render.

**Lição:** um wrapper novo em volta do jogo quebra a cadeia de altura sem aviso —
o canvas não dá erro, só passa a medir largura ÷ 2. Ao envolver a casca, conferir a
altura de todos os elos; e validar layout lendo o retângulo do canvas contra a
janela em proporções maiores e menores que 2:1.
