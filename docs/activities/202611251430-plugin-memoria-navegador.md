# Plugin de memória da Guardiã no navegador

## Investigação

A Guardiã já usava o IndexedDB para retomar turnos. Para recuperar notas
por assunto, a integração reutiliza o mesmo escopo de servidor, sala,
jogador e personagem e fornece ao host um ponto de troca em runtime.

## Alterações

O host injeta o provedor por módulo ES. O adaptador padrão do jogo guarda
uma nota por chave no localStorage. O diálogo oferece gravação e consulta
explícitas e a inferência local recebe até três correspondências.
O [guia do jardim](../USAGE-API-JARDIM.md#memória-da-guardiã-no-navegador)
documenta o contrato e a restauração do provedor.

## Validação e entrega

O publish Release passou, assim como os dois testes de
`node --test tests/guardian-memory.test.mjs`. O Chromium confirmou a
persistência após recarga, o isolamento por jogador e a troca de provedor
na página publicada. Pela interface Blazor publicada, com perfil de teste,
API indisponível e sem modelo carregado, a Guardiã gravou e recuperou uma
nota. O encaminhamento de notas à inferência foi testado com motor simulado.

O arquivo build.json do Pages identificou o commit
`c1e01fa17d12b5b2e1d4982afe1a6c7155a73bdb`.
[Publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45665089043)
e [jogo em produção](https://sufficit.github.io/sufficit-game-ultima/).
