# Posição e preferências locais do jogador

## Investigação

O jogo abria o personagem no início da trilha. Nome, leitura em voz alta,
fuso, clima, gráficos, estilo do cenário e ativação de IA já usavam
preferências locais. Contraste, HUD, opções da Oficina e o destaque do modelo
de CPU precisavam de restauração.

## Alterações

- A pose local guarda coordenadas horizontais e direção em uma chave
  por endereço da API do mundo. O motor escolhe a altura do terreno ao
  restaurar, inicia os chunks próximos e usa o posicionamento normal.
- A gravação aproveita a mensagem de pose existente e é limitada a uma
  por segundo durante o jogo. Ao ocultar ou fechar a aba, a última
  amostra pendente é gravada. Posições inválidas usam a origem.
- As opções de contraste, HUD e Oficina são restauradas do perfil local.
  A escolha de CPU já gravada passa a ser refletida no painel. Preferências
  locais continuam disponíveis offline. A captura do microfone exige ação
  explícita do jogador.
- O fallback de renderização mantém os parâmetros da URL, incluindo o
  endereço do servidor que identifica o mapa salvo.

## Validação e entrega

- Publish Release, RulesSmoke, sintaxe dos adaptadores e diálogo verificados.
- Em worker e main, o Chromium caminhou, mudou as preferências, fechou
  a aba e restaurou posição, direção, contraste, HUD, raio da Oficina
  no motor e seleção de CPU. Outro servidor começou na origem. Um
  registro de posição fora dos limites também retornou à origem.
- A sonda com posição distante (170, -145) confirmou a geração do
  terreno na área restaurada e o ajuste da altura.
- O build `b76f4cb80b0913a736c2d7ccb373eb61ed1a9fac` passou no cenário
  de posição e preferências nos dois renderizadores do
  [jogo publicado](https://sufficit.github.io/sufficit-game-ultima/).
