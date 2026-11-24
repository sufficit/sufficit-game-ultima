# Impacto e som na timeline da animação

## Investigação

A coleta registrava o recurso primeiro e disparava o gesto depois. O
tempo do efeito ficava independente do momento em que a mão chegava ao
alvo.

## Alterações

- Os clipes de coleta e escavação das DLLs declaram impacto e som em
  tempos normalizados. C# WebAssembly verifica os cruzamentos com o
  mesmo relógio usado para a pose.
- A marca de impacto entra na fila do agendador e confirma a coleta.
  A marca de som toca o efeito pelo Web Audio. A troca de ação descarta
  as marcas restantes e a pausa congela o relógio.
- O [contrato de animação](../USAGE-ANIMACOES.md) documenta o pipeline.

## Validação e entrega

- RulesSmoke verificou fronteira, pausa, loop, reinício da ação e
  encaminhamento do impacto pelo agendador fora de diálogo.
- O [teste da mochila](../../tests/backpack.e2e.cjs) confirmou a pose,
  o impacto, o efeito sonoro, três camadas de escavação, persistência,
  restauração de dados legados e deduplicação entre abas.
- Passou localmente e no build `f59bfde` com renderização no worker
  (escala 2×) e no modo principal.
  [Publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45297702328).
