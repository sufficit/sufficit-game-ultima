# t50 — Registro de conversas dos personagens (dialog-log)

**Objetivo (pedido do usuário):** salvar as conversas dos personagens em localStorage
e enviá-las periodicamente para nossa API, para análise e melhoria dos diálogos.

## Decisões

- **Um único funil de captura.** O Blazor chama `game.dialogLog.turn(npc, player, reply,
  source, ms)` no ponto onde a resposta chega à UI (`SetReply` em `ApplyDecision`,
  cobrindo roteiro do diálogo, chat da Guardiã e IA local — GPU/CPU/servidor). O método
  apenas dispara `CustomEvent 'ultima-dialog'`; qualquer outro produtor futuro usa o
  mesmo evento. A pergunta do jogador é a linha anterior (`Speaker == "Você"`) do mesmo
  diálogo. O tempo de resposta (`ms`) começa em `SubmitDialogAsync`.
- **Fila persistente**: `ultima-dialog-queue` (turnos pendentes, teto 500, gira) e
  `ultima-dialog-history` (últimos ~50 por NPC, até 24 NPCs, só depuração local).
  Id determinístico por turno (`d<timestamp36>-<fnv1a>`) → dedupe no servidor.
- **Envio**: a cada 3 min (`window.ULTIMA_DIALOG_LOG.intervalMs` configurável) +
  `pagehide`/`visibilitychange(hidden)` com `fetch keepalive`. `sendBeacon` só como
  último recurso (Blob `text/plain`), sem confirmação — coberto pelo dedupe por id.
  Retry com backoff exponencial 30 s → 10 min. Remove da fila apenas ids confirmados
  em `{ ok: true, ids: [...] }`. Desliga com `?dialogos=0`.
- **Privacidade**: mesma sanitização do nome (sem `< >` nem controles, espaços
  colapsados), textos até ~500 chars; nada de token de sessão, coordenadas ou dados de
  máquina. `player` do lote = nome já persistido em `ultima-nome`. `model` só rótulo
  público dos módulos de IA já mantidos pelo jogo.
- **Sem UI nova**: nenhum elemento permanente; apenas contador `ultima-dialog-queue-count`
  dentro do HUD de dev existente (`?dev`) e `window.game.dialogLog.debug()`.
- **Backend** (`app/DialogAnalytics.cs`): `POST /v1/analytics/dialogs` valida ≤200
  turnos/lote, sanitiza e acumula em memória com teto 20 k turnos (rotação, padrão do
  mapa); dedupe por id na última 1 h; `GET /v1/analytics/dialogs?npc=&limit=` lista
  (≤500). Anti-abuso: 60 lotes/min/IP → 429. CORS igual ao chat (`*`, mesmos headers
  do `Send`). Buffer em memória — sem disco, sem PII além do primeiro nome sanitizado.

## Cadência e limites

| Item | Valor |
|---|---|
| Envio periódico | 3 min (configurável via `ULTIMA_DIALOG_LOG`) |
| Lote máximo | 200 turnos (cliente e servidor) |
| Fila pendente | 500 turnos (gira) |
| Backoff de erro | 30 s → 10 min (exponencial) |
| Buffer servidor | 20 000 turnos (rotação) |
| Rate limit | 60 lotes/min/IP (429) |
| Dedupe | id do turno, janela 1 h |

## Pendências / observações

- Buffer do servidor é volátil (memória): reinício do pod perde o histórico acumulado;
  exportação para análise deve ser feita via GET antes.
- `beacon_sem_confirmacao`: pagehide com fetch keepalive falhando cai no sendBeacon
  sem confirmação — a fila permanece e o dedupe por id evita duplicidade no servidor.
- Turnos só saem da fila com confirmação explícita do servidor (ids ack).
