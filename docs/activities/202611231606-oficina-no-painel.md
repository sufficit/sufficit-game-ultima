# Oficina do mundo dentro do Painel do jogo

## Investigação

A captura do mantenedor mostrava a Oficina como um cartão sempre visível abaixo
do status. O Painel à direita já reunia as configurações do jogo e oferecia uma
gaveta com rolagem. O caminho solicitado usa essa entrada. A Oficina aparece
como a primeira seção para acesso direto.

## Alterações

- A expansão Oficina do mundo e suas sete seções passaram para o Painel. Foram
  usados os componentes da Sufficit.Blazor.UI e o tema já existente.
- O cartão independente e seus estilos de posicionamento saíram do mapa. A
  dica de entrada passou a indicar o novo caminho, e o botão Painel anuncia
  quando a gaveta está expandida.
- O status de cada operação continua dentro da Oficina enquanto ela está
  visível. Quando a seção ou a gaveta está fechada, o aviso aparece pelo
  snackbar.

## Validação e entrega

- `dotnet publish src/Ultima.csproj -c Release`, RulesSmoke e `git diff --check`
  passaram. Os avisos prévios de nulabilidade em `Index.razor` continuam no build.
- Chromium conferiu o artefato local e o build publicado `fec6c9b` em
  1366 × 768, 390 × 844 e 740 × 360. A Oficina ficou ausente com a gaveta
  fechada. As sete seções abriram pelo teclado e puderam ser alcançadas por
  rolagem sem transbordar na horizontal.
- A ação Levantar ativou o modo no motor e Sair do modo o desativou. O botão
  Escolher .glb abriu o seletor, sem selecionar ou enviar um arquivo. Com a
  sessão desabilitada, a tentativa de editar deu aviso dentro da Oficina
  aberta e no snackbar com o Painel fechado.
- Publicado na main e conferido no [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45244964911).
