# Controles de IA, sugestões e áudio da Guardiã

## Investigação

A captura do usuário mostrou a conversa com aviso de IA indisponível e
a caixa de texto. O DESIGN.md orientou o uso de Sufficit.Blazor.UI, a
hierarquia de ações e a preservação da janela sobre o mundo. A referência
Refero orientou agrupar os controles dentro da janela existente.

## Alterações

- A Guardiã mostra o estado do modelo local e oferece ativação ou nova
  tentativa na GPU ou CPU. O teste faz inferência curta sem solicitar
  alterações no jardim.
- Os botões de sugestão preenchem a caixa para editar antes de enviar.
  O jogador também pode digitar, ditar ou ouvir a fala atual.
- Os controles continuam no compositor declarativo e a janela pode rolar
  quando a altura disponível é pequena.

## Validação e entrega

- O teste de janelas passou em worker e main no Chromium, com respostas
  simuladas da IA, do ditado e da síntese de voz. A captura a 390 px
  confirmou a disposição dos controles no celular.
- O RulesSmoke, o build Release da suíte, o publish Release do jogo e a
  verificação de sintaxe dos adaptadores passaram.
