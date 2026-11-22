# IA de NPCs por decisões sobre snapshot com budget por tick

> **Concluído em 22/11/2026** — convertido na atividade
> [202611220040-ia-npcs-decisoes.md](activities/202611220040-ia-npcs-decisoes.md).
> Resumo: namespace `Ultima.Npc` com snapshot/eventos/decisões, cérebros por NPC
> (`DialogBrain` → Guardiã e Andarilho), agendador com fila anti-inundação e budget
> por tick, WebLLM/API como produtores externos de respostas; 34 testes no RulesSmoke
> e dois NPCs conversáveis validados na produção.

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

1. [concluído] Modelar `Snapshot` e `Decision` em C# com a Guardiã como primeiro
   cérebro (falas fixas atuais migradas para decisões). Namespace `Ultima.Npc`:
   `WorldSnapshot`, eventos (`PlayerInteracted`, `PlayerSpoke`, `ReplyArrived`,
   `IntroLinesChanged`, `DialogClosedByPlayer`), `NpcCommand`/`DialogView`/
   `NpcDecision` e `GuardianBrain` com as falas de abertura embutidas. Commit
   `86e07c6`.
2. [concluído] Tick com budget e fila de eventos limitada; testes determinísticos
   de corte e retomada. `NpcScheduler`: fila cap 32 (descarta o mais antigo),
   4 eventos/16 comandos por tick, excedente retoma no tick seguinte; batida a
   5 Hz pelo MoveTick. 13 testes novos no RulesSmoke (28 no total, todos OK).
3. [concluído] Integrar o WebLLM (`ai.js`) como produtor assíncrono de eventos de
   decisão: `AskGuardianAsync` (GPU → API → falas locais, inalterado) agora
   devolve `(reply, source)` que entra como `ReplyArrived` pela fila; a UI
   (Index.razor) virou casca que aplica decisões. Validado localmente
   (Playwright + build publicado) e em produção (build `747b50b6`): E abre,
   avanço encerra, resposta com origem "servidor · regras", Esc fecha. O caminho
   GPU (WebLLM pronto) preserva `ai.js` e o watchdog; em headless sem WebGPU a
   validação cobriu o produtor servidor/fallback.
4. [concluído] Segundo NPC provando que o desenho escala sem mudar o loop.
   Andarilho (commit `762dfd4`): `DialogBrain` base genérica com
   `GuardianBrain`/`WandererBrain` finas; `NpcScheduler` registra cérebros por
   id e roteia (diálogo aberto tem prioridade; Guardiã ganha quando os dois
   estão perto; conteúdo remoto é da Guardiã); `WorldSnapshot` ganha
   `PlayerNearWanderer`; game.js spawna o Andarilho (character.wanderer,
   fallback JS verde) circulando a clareira e reporta proximidade
   (`OnWandererNear`). 6 testes novos (34 no total). Validado localmente e na
   produção (build `762dfd4`): dica menciona o NPC certo, diálogo do Andarilho
   abre com as falas dele, resposta local marcada, diálogo da Guardiã
   preservado.
5. [concluído] Converter este plano em atividade:
   [202611220040-ia-npcs-decisoes.md](activities/202611220040-ia-npcs-decisoes.md).

## Próxima ação

Checkpoint 1: desenhar as classes `Snapshot`/`Decision` no papel do jogo atual
(posição do jogador, proximidade, estado de diálogo) e migrar as falas fixas.

Depende de: nada. Combina com [PLAN-WORKER-OFFSCREEN-CANVAS.md](PLAN-WORKER-OFFSCREEN-CANVAS.md)
(decisões podem rodar fora da main thread), mas é independente.
