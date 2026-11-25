# Migração dos testes de janelas declarativas e HUD para C#

## Investigação

Os dois ensaios cobriam a conversa sem sessão, o Diário, o contraste,
o aviso do servidor e o cartão de status da IA. O Chromium fixado pelo
script antigo do HUD já não estava disponível na bancada.

## Alterações

- Os cenários `declarative-windows` e `hud-status` usam o
  [runner Microsoft.Playwright](../../tests/WorldCollisionE2E).
- O HUD observa a importação do módulo de IA, mantém a solicitação pendente
  até o cartão indicar download e então a interrompe para verificar o erro.
  O cenário usa a thread principal e SwiftShader por padrão.
- Os scripts legados foram removidos. A
  [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração) registra os comandos.

## Validação e entrega

- Comparação no Chromium contra o build
  `989aec9f11df1a83515ebff19b0cc4085278cc44` do
  [site publicado](https://sufficit.github.io/sufficit-game-ultima/).
  A referência de janelas usou a espera pelo DOM para contornar o timeout
  do evento de carga completa. Passou em worker, assim como a versão C#.
  O C# também passou na thread principal.
- A referência do HUD usou o Chromium instalado com SwiftShader e
  completou 9/9 verificações. O C# confirmou o aviso por 13,9 s, o cartão
  recolhido, o download, o erro, o desligamento e a abertura de detalhes.
- Passaram restore com lock file, build Release da suíte, RulesSmoke e
  publish Release do jogo.
