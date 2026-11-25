# Conversa com histórico e relatos de diagnóstico

## Investigação

A captura mostrou apenas a fala em foco e seus controles. A janela atual do
Ultima e o tema de `DESIGN.md` orientaram a composição. O relato fica
disponível na conversa, onde o estado dos motores pode ser coletado.

## Alterações

- O painel mostra falas do NPC e do jogador em ordem cronológica, com rolagem
  própria. A abertura restaura os turnos já gravados no Diário do navegador.
  Uma atualização de falas de introdução entra na próxima abertura.
- **Reportar erro** abre um formulário com descrição opcional. A API devolve um
  protocolo e grava o relato no volume do jogo, com rotação e limites de tamanho
  e frequência. O payload contém um conjunto definido de estados e métricas.
  [Contrato dos controles e da API](../USAGE-JANELAS.md)

## Validação e entrega

- Build Release do servidor, publish Release do cliente e RulesSmoke passaram.
- O teste HTTP isolado comprovou protocolo, persistência, limitação de
  frequência e tamanho e descarte de campos fora do contrato.
- O Chromium confirmou histórico durante o diálogo e após recarregar,
  envio dos campos técnicos e retorno do protocolo com API simulada.
  O cenário passou no Pages com o build `64d0701`.
- O Sufficit Run publicou a release
  `55e686d13bc788191173fafbce3adaed72bc2f587fb35c63ba9e4128a60efcf4`.
  Uma requisição sintética recebeu protocolo e o mesmo ID foi encontrado no
  volume. O rollout preservou o PVC, a revisão 55, 32 edições e o catálogo.
  [Jogo](https://sufficit.github.io/sufficit-game-ultima/) · [API](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/manifest)
