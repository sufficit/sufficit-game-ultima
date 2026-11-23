# Menus de configuração com Sufficit.Blazor.UI: Oficina do mundo reconstruída

## Investigação

**Relato do mantenedor:** "conferir os menus de configuração, estão aparecendo com o
visual quebrado; devemos usar ao máximo sufficit-blazor-ui para evitar essas falhas".

Abrindo cada menu no jogo, no tamanho da janela do mantenedor (2305×1078), e medindo o DOM
mais a leitura independente das capturas:

- **Oficina do mundo** (HTML cru: `details`/`summary`, botões, entradas e seletores feitos
  à mão): **26 controles crus**, nenhum da biblioteca. O painel era posicionado no mesmo
  ponto do cartão de status: o status cobria o título e **interceptava o clique** no
  resumo — a Oficina não abria pelo clique. Os controles eram os padrões do navegador
  sobre o tema escuro ("Choose File / No file chosen", contador com setas, deslizador
  cinza, seletores brancos); três estilos de botão no mesmo painel; 756 px de altura sem
  rolagem (em janela de 360 px de altura, o fim ficava fora da tela).
- **Painel do jogo** (já em componentes da biblioteca): nada cortado nem fora do lugar.
- **Cabeçalhos das seções (os dois menus):** título e subtítulo com **0 px de espaço**
  entre eles. É da biblioteca na versão fixada pelo projeto (2.26.1102.2231): no código
  atual dela os dois já são empilhados com espaçamento.

Dois defeitos de função ligados ao modo worker (o padrão):

- o seletor de **Construir** vinha sempre vazio: o JS preenchia um `<select>` nativo lendo
  o estado do motor de forma síncrona, e no worker essa leitura é uma Promise;
- **Gráficos** era controlado por um script colado no `index.html` que trocava classes de
  botões por id.

Direção seguida (skill `sufficit-frontend`): tratar a biblioteca como infraestrutura
existente e usar a API pública; um `<select>` paralelo não reproduz a API, o tema e os
estados do seletor dela; não usar manipulação de DOM para estado que o Blazor controla.

## Alterações

Commits `937b103` e `5a6c21f`:

- **Oficina** vira um cartão da UI (mesmo vidro do Painel) com um `SUIExpansionPanel` por
  assunto (variações, criar, enviar, esculpir, construir, gráficos, estilo) e
  `SUIButton`, `SUIIconButton`, `SUITextField`, `SUINumericField`, `SUISelect`,
  `SUIStack`, `SUIText`. **Controles crus nos menus: 26 → 0.**
- **Coluna do canto superior esquerdo** (`.ultima-corner`): status e Oficina empilhados no
  fluxo, então nunca um cobre o outro; 12 px de folga; o conteúdo que não cabe rola dentro
  do painel. Em telas baixas (celular deitado) a Oficina aberta ocupa a coluna inteira.
- **Estado no Blazor:** raio do pincel, material, modelo a plantar, tamanho do envio,
  gráficos e estilo vivem na página e chegam ao motor por JS interop. O
  `game.workshop.refreshBuildOptions` passou a esperar o estado do motor (funciona nos
  dois modos), entrega a lista ao Blazor (`OnBuildAssets`) e repete até ela aparecer.
- **Envio de arquivo:** botão da UI + `InputFile` padrão do Blazor escondido, e
  `game.workshop.uploadBytes`. A versão fixada da biblioteca não tem componente de
  arquivo (o `SUIFileUpload` só existe a partir de 2.26.1116).
- **Cabeçalhos só com título** (Oficina e Painel): sem o parâmetro de subtítulo, que cola
  os dois textos na versão fixada; o estado da rede virou um selo (`SUIStatusBadge`) dentro
  da seção "Multiplayer e voz".
- `aria-pressed` com texto explícito nos botões de seleção (booleano direto renderiza
  atributo vazio, inválido).
- Foram removidos os estilos crus da Oficina (`.workshop-panel`, `.ws-*`) e o script
  inline do `index.html` passou a só persistir a preferência de gráficos.

**Atualização da biblioteca: testada e descartada.** Com 2.26.1117.650 o build compila,
mas os cabeçalhos continuam com título e subtítulo colados, o ícone das seções deixa de
aparecer e a densidade muda — sem ganho que justifique mexer na interface inteira do jogo
com outras frentes publicando em cima. O pacote segue em 2.26.1102.2231.

## Validação e entrega

Roteiro `ui-check` (espera real do jogo carregado), no build e repetido na produção
(build `937b103`), em cinco tamanhos — 2305×1078, 1366×768, 1920×1080 a 150%, celular
900×1600 e celular deitado 740×360:

| verificação | resultado |
|---|---|
| Oficina abaixo do cartão de status, sem cobri-lo | status termina em y=245, Oficina começa em y=257 (os cinco tamanhos) |
| cabeçalho da Oficina recebe o clique | sim; abre com clique real |
| controles crus nos menus | 0 |
| elementos fora da janela (fechada, aberta, tudo aberto) | 0 |
| tudo aberto (7 seções) | fica dentro da janela; 1156 px de conteúdo rolam dentro de 807 px |
| celular deitado: altura útil com a Oficina aberta | 73 px → **265 px** |
| seletor de construção | habilitado após a carga; abre com 9 opções; fecha com Escape; seleciona pelo teclado |
| Levantar / Sair do modo / raio / material / gráficos / estilo | chegam ao motor (estado conferido) |
| cabeçalho acessível | botão focalizável com estado expandido |

- Leitura independente das capturas (build e produção): Oficina com "sistema de design
  coerente… nenhum controle parece padrão do navegador — sem seletor branco, sem botão
  cinza, sem deslizador padrão, sem 'Choose File'"; Painel "limpo e consistente, sem
  texto sobreposto, cortado ou transbordando". Os poucos defeitos apontados nessa rodada
  foram corrigidos (subtítulos longos quebrando o cabeçalho em duas linhas, travessão no
  fim de linha, selo de rede esticado).
- Regressão no build mesclado e na produção: diálogo da Guardiã, Andarilho, E com
  remotos, câmera (dois modos, 0,00% de deriva) e layout — todos verdes.
- `tests/RulesSmoke`: todos os testes passando.

**Limites:**

- O envio de arquivo usa o `InputFile` do Blazor atrás de um botão da UI, não o
  componente da biblioteca. Subir a versão dela é decisão do mantenedor: traz o
  `SUIFileUpload` e o cabeçalho com título/subtítulo espaçado, mas muda a densidade e
  some com o ícone das seções nos testes feitos.
- Os dois botões de toque do jogo (A/B) continuam em HTML cru: são controles de jogo lidos
  por id pelo JavaScript, não menu.
- Não foi exercitado um envio real de arquivo `.glb` nem a geração por descrição (dependem
  do servidor do jogo e publicariam um modelo no mundo compartilhado); o caminho até o
  JS foi conferido, a chamada ao servidor não.

**Lição:** controle de formulário feito à mão em cima do tema da biblioteca volta a
quebrar (padrão do navegador, sobreposição, estado fora do Blazor). Na interface do jogo,
usar o componente da Sufficit.Blazor.UI e guardar o estado no Blazor; e, ao adicionar
seção na Oficina, seguir o padrão de `SUIExpansionPanel` já posto.
