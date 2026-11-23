# Texturas de grama e terra, luz, vento e chuva no Ultima

## Investigação

A trilha e o gramado usavam apenas cor e grão procedural. O vale e a trilha
quente continuaram como referências para o acabamento. O detalhe foi orientado
à escala do personagem e integrado à paleta e às edições persistidas do mundo.

## Alterações

- ImageGen produziu materiais ortográficos de grama curta e terra compactada.
  Os dois WebP de 256 × 256 somam 37.678 bytes. A composição usa máscaras de
  superfície e amostragem alinhada entre chunks. O material procedural cobre
  o carregamento inicial e falhas de imagem.
- O chão recebe luz e sombra da cena com uma base ambiente. Na chuva, o sol
  perde intensidade, a névoa aumenta e o solo ganha brilho discreto. As
  rajadas inclinam tufos e outra vegetação. Gotas finas são emitidas perto
  do jogador.
- O seletor da Sufficit.Blazor.UI fica em **Painel → Oficina do mundo →
  Gráficos → Clima**. A transição ocorre em tempo real e a preferência é
  local ao navegador.

## Validação e entrega

- Publish Release, sintaxe dos três scripts, quatro testes Node, RulesSmoke
  e `git diff --check` passaram.
- O build `8faae41` passou no Chromium com WebGL2 em worker e main no site
  publicado. Céu limpo, chuva, retorno ao céu limpo, persistência após
  recarga e pintura ficaram funcionais. O seletor também foi operado com
  setas e Enter. Em chuva foram medidas 470–476 partículas ativas nos testes.
- O teste no main confirmou inclinação variável dos tufos, intensidade do sol
  de 0,95 para 0,25, densidade da névoa de 0,0072 para 0,017 e sem alteração
  de motor ao trocar o clima. A emenda amostrada teve diferença média de
  4,85 em 255 níveis RGB.
- A simulação de download indisponível manteve a textura procedural em
  worker e main. A amostra local de montagem por chunk passou de 18,2 para
  19,1 ms no p95. Uma cópia do journal em sessão isolada preservou o poço
  aberto em −2 m e o teto da sala subterrânea de teste.
- Publicado na main e conferido no [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45248633436).
