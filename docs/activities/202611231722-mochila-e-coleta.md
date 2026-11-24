# Mochila e coleta de recursos no Ultima

## Investigação

O terreno combina a superfície gerada no navegador com edições compartilhadas.
Árvores, plantas e rochas também têm instâncias geradas localmente. As sessões
multijogador são temporárias. Para o piloto, a mochila e as marcas de coleta
ficam no navegador, em uma camada local sobre o mapa compartilhado.

## Alterações

- A mochila fica no Painel do jogo e usa os componentes da Sufficit.Blazor.UI.
  A ação Coletar fecha o painel, ativa a mira e permite clicar ou tocar em
  alvos a até 3,5 m. A mochila agrupa o que foi coletado por tipo.
- Plantas, árvores, pedras, objetos colocados, água pintada e rocha de grutas
  podem ser recolhidos. Retirar uma porção da superfície rebaixa o terreno e
  expõe a terra sob a grama. Jogadores e NPCs continuam interativos.
- O IndexedDB guarda o item e a marca de coleta na mesma transação. O registro
  de um alvo é único, preserva a remoção após recarregar e sincroniza as abas
  do mesmo navegador. O mapa compartilhado e as sessões de outros jogadores
  mantêm sua própria visão do cenário.

## Validação e entrega

- O publish Release, a sintaxe JavaScript e o `git diff --check` passaram.
  O [ensaio de colisão e passo articulado](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs)
  passou localmente nos modos main e worker.
- O [ensaio da mochila](https://github.com/sufficit/sufficit-game-ultima/blob/a7609116e65b74cc35707c54669ada5a2c5576a2/tests/backpack.e2e.cjs) passou localmente e no
  build publicado `ab016e7`, por clique em main e por toque em worker.
  Confirmou a remoção do toco, o desbaste de 0,48 m na malha, o alcance, a
  rejeição de duplicatas, a recarga e o registro único em duas abas.
- Um ensaio adicional recolheu água pintada e removeu as duas malhas de um
  pinheiro em uma coleta. Os ensaios usaram fixtures no navegador e rede de
  jogo desativada para manter o mundo compartilhado intacto.
- [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow bem-sucedido](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45258222905).
