# Ação de iluminar o jardim

## Investigação

O botão “Iluminar o jardim” só escrevia um pedido na caixa de texto.
Para instalar as luminárias, o jogador precisava enviar o texto e obter
um plano executável da IA.

## Alterações

- Um clique consulta o mapa e usa a família de decoração da API para
  instalar as luminárias que faltam, uma de cada lado da entrada.
- A Guardiã confirma o resultado e aplica o mapa retornado pela API à
  cena. Sem sessão, o diálogo informa que precisa de conexão. A ação
  preserva o rascunho e mantém as sugestões de trilha e flores.
- A consulta ao mapa evita duplicatas em cliques sucessivos.

## Validação e entrega

- Publish Release, RulesSmoke e testes do diálogo aprovados.
- A API isolada recebeu a ação em worker e main. O teste verificou as
  duas luminárias no mapa, os dois pontos de luz habilitados junto às
  malhas e a revisão intacta após o segundo clique.
- O teste offline conferiu o aviso de sessão na conversa.
- No [jogo publicado](https://sufficit.github.io/sufficit-game-ultima/),
  o build `67bdf0b600e1031a54623575b182903755526e0c` passou no teste
  de diálogo em worker e main. O cenário de iluminação passou em
  worker com a API em loopback. O Chromium de teste liberou o acesso
  à rede local para essa execução. Após as asserções, a API de teste
  excedeu o prazo de 10 segundos para encerrar. O jardim compartilhado
  foi preservado.
