# IA de NPCs por decisões sobre snapshot com budget por tick (Guardiã e Andarilho)

## Investigação

O diálogo da Guardiã vivia inteiro na UI (Index.razor): estado das falas, avanço,
pensando e resposta eram campos do componente, misturados com UI. A referência veio
do bot do lineage-rust: `Brain::decide(env)` sobre um snapshot imutável do mundo,
com VM de scripts sob orçamento de ops por step e fila de eventos com limite
(`killer-bot-core/brain.rs`, `killer-bot-script/vm.rs`). A WebLLM da Guardiã
(`ai.js`, GPU do jogador) e a API de conteúdo são produtores caros e assíncronos —
o desenho certo é orquestrá-los como eventos, não deixá-los no caminho do tick.

## Alterações

Dois commits na `main`:

- `747b50b` — estrutura e migração da Guardiã:
  - Namespace `Ultima.Npc`: `WorldSnapshot` (projeção imutável), eventos
    (`PlayerInteracted`, `DialogClosedByPlayer`, `PlayerSpoke`, `ReplyArrived`,
    `IntroLinesChanged`), comandos (`OpenDialog`, `ShowLine`, `CloseDialog`,
    `BeginThinking`, `SetReply`) com `DialogView`/`NpcDecision`;
  - `GuardianBrain`: máquina de estados do diálogo com as falas de abertura;
  - `NpcScheduler`: fila cap 32 (descarta o mais antigo), 4 eventos/16 comandos
    por tick, excedente retoma no tick seguinte; batida a 5 Hz pelo MoveTick;
  - Index.razor virou casca: aplica decisões, falas remotas chegam como
    `IntroLinesChanged`, `AskGuardianAsync` (GPU → API → falas locais, inalterado)
    devolve `(reply, source)` que entra como `ReplyArrived`.
- `762dfd4` — segundo NPC, provando a escala:
  - `DialogBrain` base genérica; `GuardianBrain`/`WandererBrain` ficaram finas;
  - `NpcScheduler` registra cérebros por id e roteia eventos: diálogo aberto tem
    prioridade, Guardiã vence quando os dois estão ao alcance, conteúdo remoto é
    da Guardiã, falas/respostas vão ao diálogo aberto;
  - `WorldSnapshot.PlayerNearWanderer` + `GameInterop.OnWandererNear`;
  - game.js: Andarilho (character.wanderer com fallback JS) circulando a clareira
    inicial, dica de conversa mostra o NPC certo;
  - Andarilho responde com falas locais próprias (os produtores atuais têm a
    persona da Guardiã; produtor próprio dele é conteúdo futuro).

## Validação e entrega

- RulesSmoke: 13 testes na primeira leva + 6 do Andarilho — **34 testes, todos
  passando** (abrir/avançar/encerrar, pensando/anti-inundação, resposta com
  origem, troca de conteúdo, roteamento com dois NPCs, prioridade, budget por
  tick e transbordo da fila).
- `dotnet publish -c Release` sem erros nas duas levas.
- Local (build publicado, Playwright): diálogo E2E com resposta da API
  ("servidor · regras") e, no segundo NPC, dica + diálogo + resposta local.
- Produção (builds `747b50b` e `762dfd4`,
  https://sufficit.github.io/sufficit-game-ultima/): teleport até a Guardiã e
  caminhada até o Andarilho — dica correta, diálogo do Andarilho com as falas
  dele, resposta local marcada, diálogo da Guardiã preservado, Esc fecha.
  Caminho GPU (WebLLM pronto) preservado; em headless sem WebGPU a validação
  cobriu os produtores servidor/falas locais.
- Plano convertido: [PLAN-NPC-AI-DECISIONS.md](../PLAN-NPC-AI-DECISIONS.md)
  concluído nos 5 checkpoints.
