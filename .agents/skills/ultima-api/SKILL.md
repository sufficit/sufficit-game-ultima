---
name: ultima-api
description: Joga Ultima via HTTP e descobre as famílias de comandos. Use para criar sessão, mover, conversar pela IA da Guardiã, consultar mapa e combinar famílias do jardim em uma edição.
---

# API do Ultima

Use a URL da instância do jogador. Consulte `GET /v1/game/garden` para
descobrir a área e o catálogo atuais. Crie uma sessão em `POST /v1/game/sessions`
com `{"name":"Viajante"}` e envie seu token em `X-Game-Token`.

- Jogo, posição e interação usam `POST /v1/game/command`. Observe
  `GET /v1/game/state`, `GET /v1/game/map` e `GET /v1/game/events`.
- A Guardiã aceita `{"message":"…"}` em `POST /v1/game/garden/dialog` com
  o personagem a até 4,5 m dela. `ai_unavailable` indica que o provedor
  do servidor está indisponível. Uma resposta só confirma alterações se
  `applied` for maior que zero.
- Para compor terreno, plantas e decoração, envie todas as ações numa
  única chamada `POST /v1/game/garden/edits`. Use as skills das famílias
  para montar cada ação.
- Leia [contratos e exemplos](../../../docs/USAGE-API-JARDIM.md) para
  erros, limites, replay, sessões e as outras famílias da API.
