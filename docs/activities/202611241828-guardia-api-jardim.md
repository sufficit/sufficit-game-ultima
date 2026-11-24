# Guardiã e API do jardim

## Investigação

A Guardiã oferecia variações locais por palavra-chave. A Oficina já tinha
controle de sessão e persistência. A conversa agora usa um provedor pronto
para compor ações validadas pelo servidor, e clientes HTTP podem enviar
as mesmas ações diretamente.

## Alterações

- O catálogo do jardim expõe terreno, plantas e decoração. Um pedido pode
  combinar pintura, relevo, caminhos, luminárias e colocação ou substituição
  de vegetação e objetos. O lote gera um registro no journal.
- O cliente aplica o mapa confirmado na resposta e os demais clientes
  recebem os eventos usuais. As luminárias compartilham geometria e há até
  duas luzes locais ativas perto do personagem.
- Foram criadas quatro skills do projeto em [.agents/skills](../../.agents/skills)
  e o [contrato HTTP com exemplos](../USAGE-API-JARDIM.md). O piloto cobre a
  área do jardim e os modelos registrados. Plantas nativas ficam fora da
  remoção por UID. A escavação avançada continua nas ações da Oficina.

## Validação e entrega

- A integração HTTP com provedor determinístico verificou autorização,
  proximidade da Guardiã, validação atômica, caminho, decoração, substituição,
  evento ao observador, histórico, indisponibilidade, JSON inválido e replay.
- O Chromium confirmou a recusa sem IA e a aplicação do pedido na própria
  conversa em worker e main, com malha da luminária, luz e flores na cena.
  O diálogo e o Diário também passaram no cenário sem sessão.
- Passaram RulesSmoke, a integração do WorldStore, os dois publishes em
  Release e a validação das quatro skills.
- A release `bee6e9e2b94463490f3a48308ca707f5fab244b8f66fd21a3eb06be208e3e66c`
  no Sufficit Run manteve o PVC, a revisão 53, 32 edições, 17 colocações e um
  asset dinâmico. O [catálogo público](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/game/garden)
  retornou as três famílias. A instância pública está sem provedor de IA
  do servidor configurado. A validação da IA usou um provedor de teste.
