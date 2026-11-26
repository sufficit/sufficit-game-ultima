# Design do Ultima

O mundo 3D ocupa a tela. O HUD mostra o estado da partida e a ação disponível. O
Painel do jogo concentra Mochila, Oficina do mundo e configurações, com seções
recolhíveis. A interface mantém a clareira visível durante a exploração e reserva
espaço para leitura no diálogo e no diário.

## Tema e componentes

A Sufficit.Blazor.UI fornece componentes, estados de interação e tokens. O
Ultima aplica o tema escuro de jardim e fantasia em `UltimaTheme`. O
`SUIThemeProvider` publica os tokens `--sui-*` para os componentes e para o CSS
da interface. A tipografia segue os papéis padrão da biblioteca.
[Definição do tema](src/UltimaTheme.cs) · [Design da biblioteca](https://github.com/sufficit/sufficit-blazor-ui/blob/main/DESIGN.md)

| Papel | Referência no tema | Uso |
| --- | --- | --- |
| Primário e foco | `#8fd19c` | Contornos de foco, links e acentos |
| Ação primária | `#2f6b46` com texto `#eafff2` | Ações como Enviar e Continuar |
| Secundário | `#d9c37a` | Destaques da Guardiã |
| Superfícies | `#111a14`, `#18241c`, `#203026` | Camadas dos painéis sobre o mundo |
| Texto | `#e9f1e7` e `#aec2af` | Conteúdo principal e texto de apoio |

- Componha painéis, formulários e ações com `SUICard`, `SUIStack`,
  `SUIExpansionPanel`, `SUIButton`, `SUISelect` e campos da biblioteca. Use
  `SUIStatusBadge` com texto para indicar estados como conexão, sucesso e erro.
  [Painel do jogo e Oficina](src/Pages/Index.razor)
- Use CSS do jogo para posicionar o HUD e as janelas sobre o canvas, tratar
  áreas seguras, rolagem e a superfície translúcida. Use os tokens semânticos
  da biblioteca para a aparência dos controles. O diálogo oferece uma
  superfície opaca no modo de alto contraste.
  [Estilos do jogo](src/wwwroot/css/app.css) · [Sobreposições](src/wwwroot/css/extras.css)
- Guarde seleção, expansão e valores de formulário no Blazor. Câmera, ponteiro
  sobre o mundo e controles virtuais de movimento usam a integração com as
  APIs do navegador. Diálogo e diário usam o compositor de janelas do jogo.
  [Contrato de janelas](docs/USAGE-JANELAS.md)

## Decisões e referências

| Decisão | Efeito no jogo | Referência |
| --- | --- | --- |
| Painel e Oficina recolhíveis | A exploração preserva a área de visão. Em telas baixas, o conteúdo rola dentro do painel. | [Validação dos menus](docs/activities/202611230037-menus-sufficit-blazor-ui.md) |
| Dica de interação acompanha o dispositivo ativo | O jogador vê a ação para teclado, controle ou toque. O foco deve permanecer visível ao navegar pelos menus. | [Decisão de entrada](decisions/input-v4-round.md) |
| Terreno e objetos mantêm silhuetas legíveis sob luz variável | Grama e terra pedem transições orgânicas. Luz, vento e chuva devem preservar a leitura de alvos de coleta e caminhos. | [Pesquisa visual do mundo](decisions/visual-v3-research.md) |
| A janela aberta isola o jogo | Ao abrir a conversa ou o Diário, o foco entra na janela e tudo que fica atrás fica inerte; ao fechar, o foco volta ao canvas. No celular, a folha do menu fecha quando o Diário abre. | [Foco e pilha de telas](decisions/window-focus-and-screen-stack.md) |

## Evolução da Sufficit.Blazor.UI

1. **Cabeçalhos de expansão.** A versão fixada do pacote (`2.26.1102.2231`)
   exibe `Title` e `Subtitle` sem separação adequada no jogo. Mantenha a ajuda
   no corpo da seção. Ao reavaliar a biblioteca, confira títulos longos,
   presença de ícones, foco e densidade em telas estreitas.
2. **Envio de arquivos.** A Oficina usa um botão da biblioteca com `InputFile`
   do Blazor. Avalie `SUIFileUpload` em uma versão que o inclua e confira a
   seleção e o cancelamento pelo teclado e por toque, a mensagem de erro e
   a integração com o envio de GLB.
3. **Notificações sobre janelas.** O CSS do jogo reposiciona o host para evitar
   sobrepor o campo de conversa. Se o caso se repetir em outros produtos,
   proponha posicionamento configurável para o host e valide a convivência
   com chat, teclado virtual e controles na borda da tela.

Mudanças reutilizáveis de componentes e acessibilidade pertencem à biblioteca.
Reproduza o problema com a API pública no Ultima, valide a correção na biblioteca
e teste a versão candidata no jogo antes de atualizar a referência NuGet. A
identidade visual, as posições do HUD e os fluxos de jogo pertencem ao Ultima.
[Referência do pacote](src/Ultima.csproj) · [Histórico da integração](docs/activities/202611230037-menus-sufficit-blazor-ui.md)

## Validação visual

Confira o jogo com painel fechado e aberto, incluindo a Oficina expandida. Use
desktop, zoom de 150%, celular em retrato e celular deitado. Exercite navegação
por teclado, fechamento com Escape, toque e rolagem. Confira a legibilidade
sobre cenas claras e escuras e o destaque de coleta no objeto ou trecho de terreno
que a ação atingirá.
