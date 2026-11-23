# Estilos da Sufficit.Blazor.UI nos menus do Ultima

## Investigação

A captura do mantenedor mostrava cabeçalhos cinza e com larguras diferentes na
Oficina. A [migração anterior](202611230037-menus-sufficit-blazor-ui.md) já havia
adotado os componentes da biblioteca. No build `1d7a9ca`, o HTML carregava apenas
o CSS global. O arquivo `Ultima.styles.css` era gerado e servido, mas a página
não o solicitava. Ele importa o bundle com as regras isoladas da
`SUIExpansionPanel`. O cabeçalho medido tinha 160 px dentro de 306 px disponíveis,
com `display: inline-block` e fundo cinza do navegador.

## Alterações

- O HTML passou a carregar `Ultima.styles.css` após o CSS global e antes do tema
  específico do jogo, seguindo o padrão do Showcase da biblioteca. O
  identificador do build versiona o link. A versão da dependência segue fixada.
- Em telas de até 480 px de altura, a coluna da Oficina aberta ocupa o espaço
  entre as margens da janela e fica à frente do aviso de boas-vindas. A rolagem
  continua dentro da Oficina.

## Validação e entrega

- O publish Release e RulesSmoke passaram. O artefato contém o HTML, a folha
  de entrada e o CSS isolado referenciado por ela. O build manteve os avisos
  prévios CS8601 e CS8625 de `Index.razor`.
- Em produção, Chromium confirmou HTTP 200 para as duas folhas. O cabeçalho
  passou a `display: flex`, com altura mínima de 44 px e largura de 306 px de
  306 px disponíveis no desktop. Ícones, rótulos, divisórias e setas foram
  conferidos nas capturas.
- Oficina e Painel testados em 1366 × 768, 390 × 844 e 740 × 360. Todas as
  seções da Oficina abriram com Enter e o cabeçalho fechou com Espaço. O
  seletor de material abriu com Enter, exibiu suas opções e fechou com Escape.
  Não houve rolagem horizontal. Em 740 × 360, a área de rolagem passou de 139
  para 334 px e o título ficou visível durante o aviso. Os testes não
  acionaram ações que alteram o mundo ou enviam arquivos.
- Builds `34e807c` e `9201842` publicados na main. O segundo foi conferido no
  [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45231218898).
