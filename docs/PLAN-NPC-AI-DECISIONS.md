# IA de NPCs por decisões sobre snapshot com budget por tick

## Objetivo e critérios

Estruturar a IA dos NPCs (hoje: Guardiã com falas fixas + `ai.js` WebLLM) como
**produtores de decisões sobre um snapshot do mundo**, com orçamento de
execução por tick. Nenhum NPC lê nem muta o mundo vivo: recebe uma projeção
imutável, decide, devolve comandos/eventos que o loop aplica.

Critérios de aceite:

- `NPCBrain.decide(snapshot) -> Decision { comandos, eventos }` implementado em
  C# (`src/Rules`), testável sem navegador (testkit no estilo de
  `tests/RulesSmoke`).
- Loop de tick com budget por NPC: decisão excedente é cortada e retoma no tick
  seguinte; um NPC nunca bloqueia o frame.
- Fila de eventos por NPC com limite anti-inundação (aviso em tempo real, input
  do jogador, respostas da LLM).
- WebLLM da Guardiã entra como mais um produtor de decisões, orquestrado pelo
  mesmo pacer: resposta cara chega assíncrona e vira evento, não trava o tick.
- Fallback determinístico atual (falas fixas) permanece o caminho sem IA.

## Terreno e referências

- Fonte: o bot do lineage-rust separa `Brain::decide(env, chat)` sobre snapshot
  (`killer-bot-core/{brain,env,snapshot,testkit}.rs`) e roda scripts numa VM
  com budget de ops por step e fila de eventos limitada
  (`killer-bot-script/vm.rs`: tasks, `wait`/yield, `step(host, now)` cortado
  por orçamento).
- Decisão de projeto análoga já usada aqui: o diálogo vive em C# e a ponte é
  JSInterop; o padrão Decision fecha bem com isso.
- Fila de eventos anti-flood é o mesmo problema do controle de avisos da API de
  conteúdo (`app/main.py`).

## Checkpoints

1. [pendente] Modelar `Snapshot` e `Decision` em C# com a Guardiã como primeiro
   cérebro (falas fixas atuais migradas para decisões).
2. [pendente] Tick com budget e fila de eventos limitada; testes determinísticos
   de corte e retomada.
3. [pendente] Integrar o WebLLM (`ai.js`) como produtor assíncrono de eventos de
   decisão; validar status/watchdog existentes.
4. [pendente] Segundo NPC (ex.: fauna simples) provando que o desenho escala
   sem mudar o loop.
5. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1: desenhar as classes `Snapshot`/`Decision` no papel do jogo atual
(posição do jogador, proximidade, estado de diálogo) e migrar as falas fixas.

Depende de: nada. Combina com [PLAN-WORKER-OFFSCREEN-CANVAS.md](PLAN-WORKER-OFFSCREEN-CANVAS.md)
(decisões podem rodar fora da main thread), mas é independente.
