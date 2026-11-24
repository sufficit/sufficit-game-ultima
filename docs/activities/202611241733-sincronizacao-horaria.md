# Sincronização horária do relógio GMT

## Investigação

O painel e o céu usavam o UTC do dispositivo. O servidor já devolvia
a hora no estado da sessão, que exige autenticação e leva o mapa junto.
O relógio precisa funcionar também fora de uma sessão multiplayer.

## Alterações

- A API pública devolve `utcMs` em `/v1/worlds/ultima/time` e marca a
  resposta com `no-store`.
- O cliente consulta a rota na abertura e uma vez por hora. Ele soma
  à amostra metade do tempo de ida e volta. O painel e o céu continuam
  a partir da última calibração usando relógios monotônicos.
- Sem primeira amostra, o dispositivo fornece o UTC inicial.
  O fuso local continua reservado à apresentação.
  [Guia do mundo](../USAGE-MUNDO.md#relógio-céu-e-clima)

## Validação e entrega

- API e cliente compilados em Release. As 169 verificações C# passaram.
- O Chromium verificou um desvio de nove horas, nenhuma nova consulta
  antes de uma hora, a manutenção da calibração após HTTP 503 e a
  aplicação da amostra seguinte já disponível. O céu e o painel
  usaram a mesma referência no worker e na thread principal.
  [Teste de relógio](../../tests/world-clock.e2e.cjs)
- Uma resposta simulada com 1,2 s de atraso produziu 648 ms de ajuste
  na hora enviada ao motor, incluindo o tempo de processamento.
- O site publicado no build `78b135e8411d9d1d974f86d209f3c254a859015c` recebeu a hora do servidor
  real nos dois modos. A API foi publicada no Sufficit Run na release
  `8cf7983b60347585bc73d74acc61923fcbcccaffc5bb9c92946aec05f13f8f05`.
  A verificação de publicação confirmou o mesmo PVC, a revisão 53 do
  mapa, 32 edições e 1 asset no catálogo.
  [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)
  · [Hora da API](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/worlds/ultima/time)
