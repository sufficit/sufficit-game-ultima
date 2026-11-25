# Reconhecimento da IA local pela Guardiã

## Investigação

A CPU carregava o modelo, mas descartava a preferência de ativação ao
recarregar. O diálogo consultava o estado recebido por notificações do
motor e exigia uma sessão até para conversar sem editar o jardim.
Quando o modelo local falhava e o servidor estava sem IA, a mensagem
também orientava a ativar um modelo que já estava pronto.

## Alterações

- A Guardiã consulta o estado real do motor ao abrir a conversa, concluir
  a ativação, testar ou enviar um pedido.
- A CPU guarda a preferência após o carregamento e restaura o modelo na
  próxima abertura. O contexto de 4.096 tokens comporta as instruções do
  jardim e o plano de alterações.
- O modelo local conversa sem sessão. As edições passam pela API, que
  valida o plano e grava o mundo. Um plano inválido é distinguido de IA
  desativada quando o servidor não tem provedor alternativo.

## Validação e entrega

- Publish Release, RulesSmoke e sintaxe do adaptador de CPU aprovados.
- No Chromium, o teste de janelas passou em worker e main com estado
  simulado independente das notificações. O teste com API isolada
  validou a mensagem para plano inválido, o mapa intacto nessa situação
  e a aplicação de um plano válido vindo da CPU.
- O Gemma 3 270M real respondeu em português pela CPU e voltou a
  carregar após atualizar a página. Desligar removeu a preferência.
- A API isolada demorou mais de 10 segundos para encerrar após os testes
  de navegador. As asserções passaram em worker e main. O teste direto
  da API também terminou com sucesso.
- O GitHub Pages serviu o build `3bbba11972106ec225a4218b982c59eed7e6b78f`.
  O teste de janelas passou nos dois renderizadores no
  [jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
