---
name: ultima-garden-decor
description: Coloca e remove luminárias e objetos do catálogo no jardim de Ultima por HTTP. Use para decorar trilhas, entradas e clareiras.
---

# Decoração do jardim

Consulte `families.decor.assets` em `GET /v1/game/garden` e o catálogo do
mundo em `GET /v1/assets`. Envie `{"actions":[…]}` a
`POST /v1/game/garden/decor` com o token do jogador.

- Para iluminar, use `{"action":"place","asset":"prop.lantern","x":2,"z":-12}`.
  A posição é no solo. A aparência emissiva é permanente, e o renderizador
  ilumina a região com as duas luminárias mais próximas do personagem.
- Para remover um elemento, descubra o `uid` em `GET /v1/game/map` e
  envie `{"action":"remove","uid":"…"}`.

For a confirmed relative adjustment to nearby lanterns, call
`POST /v1/game/garden/tools` with the player token, tool name
`adjust_lantern_height`, and `arguments.delta_m` (signed metres).
A successful receipt reports `applied` and the resulting map.

Veja o [contrato do jardim](../../../docs/USAGE-API-JARDIM.md) para
composições que usam outras famílias ou modelos publicados no catálogo.
