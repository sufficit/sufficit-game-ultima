# Decisão: Marco 3 — Ultima jogável por API (API-first)

Data: 2026-11-17 · Status: aprovado pelo usuário (mensagem de 2026-11-18 02:52 UTC)

## Atualização 2026-11-18 03:32 UTC — F1+F2 IMPLEMENTADAS E VALIDADAS (13/13 local)

Contrato real em produção no `app/main.py` (todas as rotas testadas contra servidor local):

| Método | Rota | Função |
|---|---|---|
| POST | `/v1/game/sessions` `{name}` | cria sessão → `{token, character, world, eventsFrom}` (personagem nasce em (0,8)) |
| GET | `/v1/game/sessions` | lista personagens online |
| GET | `/v1/game/state` (header `X-Game-Token` ou `?token=`) | mundo, personagens, NPC, mapa+jobs, `eventsFrom` |
| POST | `/v1/game/command` `{action, ...}` | `move{dx,dz,dur}` · `moveto{x,z}` · `look{yaw}` · `stop` · `say{text}` · `interact` · `dig{cx,cz|x,z}` · `stop_job` · `start_job` |
| GET | `/v1/game/events?since=N&wait=S` | long-poll de eventos (percepção do agente): `join/leave/say/npc/snapshot/job_started/job_done/job_cancelled` |
| GET | `/v1/game/map` | mapa persistido `{revision, tiles, jobs}` |
| POST | `/v1/game/map/edits` (header `X-Game-Token` ou body `token`) | aplica até 100 edições atômicas `{edits:[{cx,cz,type}]}`; tipos `path`, `water`, `rock`, `pit`, `erase`; incrementa `revision`, persiste e emite `map_edited`; células com job ativo respondem 409 |
| POST | `/v1/game/leave` | encerra sessão |

Constantes do mundo espelham o cliente (game.js): `WORLD_HALF=48`, `WALK_SPEED=3.5`, Guardiã em `(0,-14)`, célula editável de 4 m.

Automação (t36, núcleo pronto): jobs de escavação progridem por tempo de parede no tick do servidor (10 Hz) — **sem novos comandos do dono** — e viram tile `pit` no mapa com `revision` monotônico, persistido em `app/data/map.json` (atomic write). `snapshot` a cada 2 s nos eventos dá progresso dos jobs para acompanhamento.

Validação local (`/tmp/ultima_test.py`): 13/13 PASS — inclui Guardiã respondendo `say` via motor de regras (t32) dentro de `talkRadius 4.5`, dig duplicado → 409, `job_done` + tile `pit` + revision no mapa após 30 s.

Agente de referência: `scripts/agente.py` (stdlib puro) — join, andar até a Guardiã, conversar, iniciar escavação, escutar eventos em long-poll. Uso: `python3 scripts/agente.py --api <url> --nome Agente`.

Pendências: F4 (cliente Blazor sincroniza e renderiza personagens remotos/tiles), canal wss nativo (hoje: eventos long-poll + áudio P2P WebRTC DTLS/SRTP já validado no voice.js).

## Requisitos do usuário (íntegra)

> quero que procure skills para nos ensinar a lidar com as testuras e modelos 3d;
> quero que o jogo seja completamente jogavel via API, quero poder me conectar via api, colocar um agente virtual dando comandos para o seu próprio personagem e sendo capaz de entender o que ele esta fazendo no jogo, com websoket ou cable criptografado para conversas de texto e audio com o agente via api;
> quero poder por exemplo ter uma mapa editavel e compartilhado, persistido em suas alterações, onde um personagem possa iniciar por exemplo um trabalho de escavação no terreno. que via api as ordens aos personagens tivesse algo tipo de automação para facilitar o acompanhamento.

## Objetivo

1. Jogo **totalmente jogável via API**: um agente virtual conecta, controla o próprio
   personagem e **entende o que está acontecendo** no mundo (percepção + eventos).
2. **Mapa editável e compartilhado**, com alterações **persistidas**.
3. **Trabalhos no terreno** iniciados por personagens (ex.: escavação) com progresso.
4. **Automação de ordens** via API para acompanhar o personagem sem comandar cada passo.
5. Canal de conversa **texto + áudio criptografado** com o agente.

## Princípios

- O cliente Blazor WASM continua funcionando **offline** (Marco 1 intacto). A API é
  **fonte da verdade opcional**: quando online, o cliente sincroniza com o mundo
  autoritativo do servidor.
- O **mesmo contrato** serve para jogador humano (navegador) e agente (REST/long-poll).
  Nada de camada só-para-bots.
- Mundo autoritativo roda no backend Python já publicado (app Sufficit, HTTPS/TLS).
  Persistência em arquivo JSON versionado sob `data/` (volume declarável no app.json
  quando necessário sobreviver a restarts).
- Sem dependências externas: Python stdlib, como o resto da API.

## Camadas

```
Agente (LLM/script)      Jogador humano (navegador)
        │ REST + long-poll/SSE        │ Blazor WASM + Babylon (render)
        ▼                             ▼
        └──────────► API Ultima (app/main.py) ◄──────────┘
                     mundo autoritativo em grade + jobs com tick
                     mapa persistido (data/map.json) + eventos
```

## Contrato de API (v1/game, fatia F1)

| Método | Rota | Função |
|---|---|---|
| POST | `/v1/game/sessions` | Cria sessão de personagem; retorna `token` opaco |
| GET | `/v1/game/state?token=` | Estado completo: posição, direção, mapa, jobs visíveis, visinhos |
| POST | `/v1/game/command` | Um comando: `move`, `look`, `interact`, `say`, `start_job`, `cancel_job` |
| GET | `/v1/game/events?token=&wait=` | Long-poll de eventos (percepção do agente) |
| GET | `/v1/map` | Mapa compartilhado (grade + revisão) |
| POST | `/v1/map/edits` | Edita células (persistido; requer token) |
| GET | `/v1/jobs?token=` | Trabalhos do personagem com progresso |
| POST | `/v1/agents/{id}/message` | Conversa texto com o agente dono do personagem |

Comandos de movimento usam a mesma semântica do cliente (move contínuo por intenção:
`{"cmd":"move","dir":"forward","secs":0.5}`), para um agente poder "segurar W" via API.

## Percepção do agente

- `GET /state` devolve: posição, orientação, células do mapa ao redor (raio), NPCs e
  jogadores próximos, jobs em andamento com % concluído, últimas falas ouvidas.
- `GET /events` (long-poll) empurra: mudanças de posição de vizinhos, conclusão de
  jobs, edições no mapa, falas próximas (chat), avisos do mundo.

## Texto e áudio com o agente

- **Texto**: POST `/v1/agents/{id}/message` + long-poll `/events` (TLS de ponta a
  ponta pelo HTTPS do app Sufficit). 
- **Áudio**: reutilizar a sinalização WebRTC já existente (`/v1/net/*`) — o agente
  vira um peer: RTP/DTLS criptografado direto com o jogador, sem mídia passando pelo
  servidor. A camada `voice.js` já suporta offer/answer/ICE/tracks.
- WebSocket puro (RFC 6455) com stdlib é possível mas caro; decisão: **long-poll/SSE
  primeiro** (mesma semântica, zero dependências), WS só se medirmos necessidade real.

## Automação (fila de ordens) — implementado e validado (19/19 testes)

- `command {action:"queue", cx, cz}` enfileira até **8 ordens** de escavação por
  personagem; duplicadas/células ocupadas/já escavadas retornam 409 (`order_exists`,
  `job_exists`, `already_dug`); excedente retorna 429 (`queue_full`).
- O **tick do servidor** executa a fila sozinho: sem job ativo, o personagem caminha
  até a célula (`order_walking`), chega e inicia o trabalho (`job_started`) sem novos
  comandos do dono. Falhas (muito longe, célula ocupada) emitem `order_failed`.
- Acompanhamento: `GET /state` devolve `orders` pendentes e `me.orderCount`;
  `command {action:"cancel_order", id|"all"}` remove ordens; `stop_job` para o
  trabalho e esvazia a fila.

## Fatias verificáveis

- **F1** Núcleo headless: sessões, grade, movimento, `state`, `command`, `events`
  (long-poll), tudo determinístico e testável por curl/Playwright.
- **F2** Mapa editável persistido + escavação (`jobs`) — persistência em `data/map.json`.
- **F3** Automação: fila de ordens e resumos.
- **F4** Cliente sincroniza com o mundo autoritativo quando online (render do estado
  do servidor; fallback offline intacto).
- **F5** Agente de ponta a ponta: script agente real joga via API (andar até a Guardiã,
  conversar, iniciar escavação, relatar o que viu) — critério de aceite do marco.

## Riscos

- Persistência real precisa de volume no app.json (storage) se o pod reiniciar;
  F2 começa em arquivo e o volume é declarado ao publicar.
- Concorrência de edições no mapa: revisão monotônica + última-escrita-vence por célula
  na F2; merge real só se dois editores simultâneos se tornarem caso real.
