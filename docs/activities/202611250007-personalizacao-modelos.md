# Personalização de modelos por instância

## Investigação

O servidor aceitava parâmetros específicos de luminárias. A geometria de
um GLB e sua instância na cena precisavam permanecer separadas para
alterar um objeto sem modificar os demais que usam o mesmo modelo.

## Alterações

- A interface `IEditablePlacementModel` em C# declara campos, unidade,
  limites e padrões e aplica a alteração parcial. A API descobre esse
  contrato em `models` e registra apenas valores validados.
- O contrato comum atende qualquer modelo colocável, inclusive importado,
  com escala por eixo e rotação horizontal. A luminária declara também os
  parâmetros de haste e intensidade. Os arquivos do contrato no Core e
  no servidor independente foram comparados byte a byte.
- `place` cria uma variante usando o asset de origem. `customize` mantém
  o UID e a posição. O mapa e o replay conservam as duas operações. A cena
  compartilha a geometria e reconstrói somente a instância afetada quando
  há mudança geométrica, com atualização de colisão. Atualizações que
  chegam antes do carregamento do GLB preservam o estado mais recente.
- O planejador recebe as definições e os valores atuais das instâncias.
  Um GLB importado oferece os controles comuns. A edição de uma parte
  específica depende de parâmetros implementados pelo modelo.
  [Contrato da API](../USAGE-API-JARDIM.md)

## Validação e entrega

- Release do servidor e do cliente, compilação dos pacotes, RulesSmoke e
  sintaxe do renderizador aprovados.
- A API verificou upload aprovado por hash, variante importada, alterações
  parciais, rejeição de parâmetro de luminária em uma samambaia e replay.
- O Chromium, com provedor simulado, conferiu em worker e main uma
  samambaia com o dobro da altura e rotação, ao lado de outra padrão, e
  preservou os parâmetros próprios da luminária. Após as asserções, a API
  de teste excedeu os 10 s de encerramento.
- O Sufficit Run publicou a release
  `7c1dc66cfb6224f1528772a707fd14e0286701df9ee7cff01f3ad8346236c9c3`.
  O volume manteve a revisão 55, 32 edições, as colocações e o catálogo.
  [Contrato em produção](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/game/garden)
- O build `6b7efcb914237bd6196b702834d83a47228fb09c` passou no cenário
  das variantes em worker no GitHub Pages, com API isolada em loopback.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)
