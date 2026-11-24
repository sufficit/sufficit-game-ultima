# Movimento e expressão dos personagens do Ultima

## Investigação

Jogador, Andarilho, Guardiã e avatares remotos usavam malhas com partes
agrupadas por material. Só o jogador tinha pernas articuladas. O teclado
entrava direto na corrida e os avatares remotos saltavam entre posições
recebidas. A direção visual preservou as formas e cores dos modelos
originais. O guia de movimento da Refero orientou as transições e os gestos
curtos ligados a uma ação.

## Alterações

- Teclado caminha com WASD ou setas e corre com Shift. O analógico continua
  proporcional e corre no fim do curso. O deslocamento acelera e freia
  gradualmente e a orientação do corpo acompanha a direção.
- A passada acompanha a distância percorrida. Joelhos e sapatos continuam
  usando o terreno e, para o jogador, o apoio real nas malhas de obstáculos.
  Braços alternam com as pernas, e a inclinação aumenta na corrida.
- Personagens respiram e piscam. A Guardiã, o Andarilho e o jogador se
  voltam para o interlocutor e mudam a pose entre escuta e fala. O movimento
  da boca é estimado pelo comprimento da fala, sem depender da reprodução
  de áudio. O jogador gesticula ao examinar, recolher e escavar.
- O Andarilho faz uma pausa enquanto conversa. Avatares remotos interpolam
  as posições e animam a passada com o deslocamento exibido, usando as
  mensagens de posição que o jogo já enviava.

## Validação e entrega

- Geração dos pacotes de modelos, publish Release, sintaxe JavaScript,
  `git diff --check` e RulesSmoke passaram.
- O [ensaio de movimento](https://github.com/sufficit/sufficit-game-ultima/blob/85d587cc3ae9b2cda02888721b52df8e072c478d/tests/world-collision.e2e.cjs) verificou
  caminhada e corrida, colisão, apoio dos pés, gesto de exame, expressão
  de diálogo e interpolação remota. A mesma janela de 700 ms de teclado
  produziu cerca de 2,4 m em caminhada e 4,4 m em corrida.
- O [ensaio da mochila](https://github.com/sufficit/sufficit-game-ultima/blob/a7609116e65b74cc35707c54669ada5a2c5576a2/tests/backpack.e2e.cjs) verificou o gesto
  após a coleta confirmada, o destaque e a escavação em camadas.
- Um ensaio local adicional observou o Andarilho parar de caminhar
  durante a conversa e voltar ao trajeto após o fechamento do diálogo.
- O build `72ba120` passou em produção com movimento e interações em
  main e worker, além de coleta por toque em escala 2×. O ensaio de
  velocidade espera a fila de modelos ficar vazia para medir após o
  carregamento do cenário. [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45266508239).
