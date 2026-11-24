# Migração dos testes de passo, relógio e mochila para C#

## Investigação

O passo articulado exercita o pé em balanço quando a sola do outro avatar
interfere na passada. O relógio fixa datas conhecidas e emula o servidor de
hora para controlar o avanço e a recuperação de falhas. A mochila verifica a
projeção da cena, o gesto, o som e a persistência da coleta.

## Alterações

- Os cenários `step`, `clock` e `backpack` foram integrados ao
  [runner Microsoft.Playwright](../../tests/WorldCollisionE2E), com
  `collision` como entrada padrão. As asserções e a automação do navegador
  ficam em C#. As sondas leem Babylon.js, IndexedDB e Web Audio, e os
  fixtures controlam a passada e o instante inicial do worker.
- No cenário de toque, a mochila abre pelo botão do menu móvel. O menu
  fecha para liberar o canvas e reabre para parar a coleta. O script legado
  procurava o botão do painel desktop, invisível em dispositivos de toque.
- As entradas antigas foram removidas depois da comparação. Os registros
  históricos apontam para a revisão em que ainda existiam. Os comandos e
  opções estão na [arquitetura](../../ARCHITECTURE.md#desempenho-da-migração).

## Validação e entrega

Ambas as versões foram exercitadas no Chromium contra o build
`a7609116e65b74cc35707c54669ada5a2c5576a2` do [site publicado](https://sufficit.github.io/sufficit-game-ultima/).

- Passo articulado em worker e main. As duas versões reduziram o avanço de
  0,38 m para zero, sem deriva da raiz. A primeira execução do legado em
  worker esgotou a espera de modelos e a repetição passou.
- Relógio e clima em worker e main, com telas desktop e móvel. Passaram o
  ciclo solar e lunar, a persistência do fuso e da neblina, a chuva e a
  sincronização horária com falha HTTP 503 e recuperação.
- Mochila em worker e main com mouse a 1× e toque a 2×. A referência de
  toque usou uma cópia temporária do legado com a navegação pelo menu móvel.
  As verificações cobriram a prévia com mouse, o som, o gesto, a escavação
  de 1,44 m em três camadas, a compatibilidade com a escavação antiga, a
  persistência e a coleta atômica em duas abas.
- A suíte de colisão também passou em worker e main. O build Release da
  suíte, o restore com lock file, o RulesSmoke e o publish Release do jogo
  passaram.
- O contato lateral da suíte de colisão oscilou em main a 960×540,
  inclusive na cópia da revisão anterior. A execução em 1200×720 com
  `ULTIMA_WALK_SCALE=1.5` mediu 0,132 m e completou a suíte. O limite de
  0,14 m foi preservado.
