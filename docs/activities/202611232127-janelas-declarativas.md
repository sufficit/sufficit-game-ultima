# Diálogo e Diário declarados em JSON

## Investigação

O diálogo em Razor já usava Sufficit.Blazor.UI e separava as decisões dos
NPCs da apresentação. Essa superfície definiu a hierarquia visual.
O Painel do jogo definiu a posição da ação que abre o Diário.
O guia `anti-ai-slop.md` da skill Refero orientou o uso de uma única
superfície por janela, com componentes da biblioteca.

## Alterações

- O mesmo renderizador Razor interpreta os layouts de conversa e Diário.
  Ambos compartilham as ações de fechar e a escolha de tema. O Diário lista
  até doze respostas de personagens do histórico local.
- A conversa conserva avanço, envio, ditado, indicação de origem e atalhos.
  Durante uma janela, os avisos transitórios passam para o topo para liberar
  os controles no rodapé. A opção de contraste usa os tokens do tema existente.
- [Contrato do layout e exemplos](../USAGE-JANELAS.md).

## Validação e entrega

- Publish Release e RulesSmoke passaram.
- O [teste de janelas](../../tests/declarative-windows.e2e.cjs) confirmou
  o Diário vazio, a troca real de contraste, o avanço da fala, o envio
  para a Guardiã, a origem da resposta e a aparição da resposta no Diário.
  O teste passou localmente e no build `c630ebb`.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)
  · [Workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45296795899).
