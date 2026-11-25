# Luminárias paramétricas da Guardiã

## Investigação

A luminária tinha geometria e intensidade fixas. O contrato do jardim tinha
colocação e remoção, sem operação para alterar parâmetros de uma instância.
Uma atualização do mapa também ignorava mudanças em um UID já na cena.

## Alterações

- O catálogo declara os parâmetros aceitos por modelo. A luminária aceita
  altura da haste e intensidade independentes. A API pode criar uma variante
  ou ajustar uma instância pelo UID. O lote valida os valores antes de gravar.
- O pedido relativo acrescenta 0,6 m ao parâmetro de altura e aumenta a
  intensidade em cerca de 25% para até duas luminárias próximas. Um pedido
  livre pode usar o mesmo contrato por meio do planejador.
- A cena reutiliza protótipos da mesma altura. Uma mudança de intensidade
  conserva a malha e move o ponto de luz apenas quando a haste muda. A
  largura da base, do vidro e da cobertura é preservada.
- O journal e o mapa carregam os parâmetros. Objetos antigos conservam
  os valores padrão, e os demais jogadores recebem a edição pelo mapa.
  [Contrato da API do jardim](../USAGE-API-JARDIM.md)

## Validação e entrega

- Release do servidor e do cliente, RulesSmoke e sintaxe do adaptador aprovados.
- A integração da API confirmou validação, lote atômico, personalização,
  evento para observador e persistência após reiniciar. A frase exata do
  jogador foi testada com o modelo de CPU simulado no Chromium em worker e
  main. Os mesmos UIDs ficaram visíveis com hastes maiores e luz de intensidade
  1,31, no lugar de 1,05. O backend isolado demorou mais de 10 s para encerrar
  após as asserções.
- O Sufficit Run promoveu a release
  `1e329876de953140f685f022e597d6d0ba2a8d784c593c0e315b8d47f3bcf536`
  com a montagem de dados preservada. O mapa manteve a revisão 55, as 32
  edições, o catálogo anterior e as colocações. Um pacote de compilação
  antigo e já concluído foi arquivado e retirado da cota temporária.
  [Catálogo em produção](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/game/garden)
- O build `2d80a7dfcba1b71f2f2220f550b0aa125d36f13f` passou no cenário
  de iluminação e personalização em worker com API isolada. O Chromium
  de teste autorizou o acesso a loopback para preservar o jardim
  compartilhado. [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)
