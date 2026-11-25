# Migração de serviços JavaScript para C# no Ultima

## Investigação

O Blazor já executava as regras do jogo e o agendador dos NPCs. O JavaScript
misturava os adaptadores do navegador com regras e estado da mochila,
Oficina, sessão HTTP, diálogos, atualização e convite. O motor 3D usa
Babylon e pode renderizar em um worker, com as interações do mundo
atualizadas no mesmo loop.

## Alterações

| Responsabilidade | C# | Ponte do navegador |
|---|---|---|
| Mochila | Identidade da coleta, contagem, sincronização da cena e feedback | Transação IndexedDB e BroadcastChannel |
| Oficina | Catálogo, geração, upload, comandos de edição e SHA-256 | Seletor de arquivo e entrega do GLB ao motor |
| Sessão do jogo | Autenticação, eventos, comandos, mapa e cadência da presença | sessionStorage, saída com keepalive e aplicação dos snapshots no motor |
| Diálogos | Saneamento, histórico, fila, reenvio e confirmação | localStorage, eventos de saída, fetch keepalive e sendBeacon |
| Atualização e convite | Comparação de builds, aviso Blazor e URL da sala | Leitura do build, recarga e clipboard |

O IndexedDB, os registros de diálogo já gravados e o contrato HTTP foram
mantidos. A interface usa Sufficit.Blazor.UI para o aviso de atualização.
O código JavaScript próprio caiu de 6.066 para 5.374 linhas, excluídas as
dependências vendorizadas.

Babylon, o worker, o loop de movimento, a geração de malhas em tempo real,
a entrada do navegador, WebRTC, voz e os runtimes WebLLM e wllama continuam
em JavaScript. Essa divisão mantém o cancelamento, a seleção e a colisão
dentro do motor, com o Blazor recebendo eventos em vez de chamadas por quadro.

## Validação e entrega

- Publish Release, RulesSmoke, sintaxe JavaScript e diff limpo passaram.
- O [ensaio de serviços do navegador](https://github.com/sufficit/sufficit-game-ultima/blob/296891feef444f5dbbb16646dfc5a8aafd13392e/tests/browser-services.e2e.cjs)
  usou uma API isolada para validar sessão e mapa, presença, chat, edição,
  criação e upload de GLB, rejeição por SHA-256, convite, aviso de atualização
  e confirmação da fila anterior após uma falha de envio.
- Os ensaios de [mochila](https://github.com/sufficit/sufficit-game-ultima/blob/a7609116e65b74cc35707c54669ada5a2c5576a2/tests/backpack.e2e.cjs) e
  [movimento](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs) cobriram as regressões do jogo.
- O build `2746bd2` passou no GitHub Pages com os três ensaios. Os testes
  de sessão e Oficina usaram endpoints simulados no navegador.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45269084687).
