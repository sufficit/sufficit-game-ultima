# Estabilização vertical da câmera

## Investigação

O alvo da câmera usava a altura do corpo depois da solução de contato dos pés.
Na revisão anterior, uma caminhada na clareira acumulou 0,540 m de variação
vertical, tanto no corpo quanto no alvo entre quadros.

## Alterações

- A câmera usa a altura do piso calculada na animação, com o apoio de objetos
  quando os dois pés estão sobre eles. O filtro em C# amortece desníveis,
  limita o atraso da altura a 24 cm e recentra em teletransportes.
- A órbita e o teste contra terreno, vegetação e túneis usam o mesmo alvo.
  O filtro aproveita a chamada WASM de atualização de câmera existente.

## Validação e entrega

- Release, RulesSmoke e os cenários Chromium de câmera e túnel passaram
  localmente no worker e na main thread.
- A publicação `f5403dd` foi confirmada no GitHub Pages. Durante a caminhada
  medida nessa publicação, a variação vertical acumulada do alvo foi
  0,00009 m no worker e 0,014 m na main thread. O corpo acumulou
  0,500 m e 0,795 m, respectivamente.
- O teste também confirmou órbita, zoom, recuo de 7,50 m para 1,25 m contra
  os pinheiros e recuperação do raio após a remoção.
  [Jogo](https://sufficit.github.io/sufficit-game-ultima/) · [Deploy](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45703894760)
