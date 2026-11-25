# Migração do teste de serviços do navegador para C#

## Investigação

O ensaio original verificava a integração dos serviços Blazor com o
navegador e uma API simulada. O caso reúne chamadas autenticadas, mapa,
Oficina, Diário, aviso de atualização e convite de multiplayer.

## Alterações

- O cenário `browser-services` passou ao
  [runner Microsoft.Playwright](../../tests/WorldCollisionE2E).
- A API de teste é simulada em C#, com respostas isoladas na origem do
  site. O C# captura os comandos, arquivos enviados e lotes de diálogo.
  As sondas JavaScript consultam o Babylon.js, o armazenamento e o clipboard.
- O script legado foi removido. O comando está na
  [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração).

## Validação e entrega

- O legado passou e o C# passou em worker e main contra o build
  `296891feef444f5dbbb16646dfc5a8aafd13392e` do
  [site publicado](https://sufficit.github.io/sufficit-game-ultima/).
- Passaram o aviso de atualização, a restauração da fila, a deduplicação
  e sanitização do Diário, a retenção dos registros após 503 e a
  confirmação no reenvio. Sessão, mapa, pose, chat e edição também passaram.
- A Oficina aceitou o GLB com SHA-256 válido e descartou o inválido. Passaram
  a geração, o upload binário autenticado e a cópia de convite com sala e API.
- Build Release da suíte, restore com lock file, RulesSmoke e publish Release
  do jogo concluíram com sucesso.
