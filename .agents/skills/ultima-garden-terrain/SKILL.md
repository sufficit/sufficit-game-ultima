---
name: ultima-garden-terrain
description: Edita superfície, relevo e caminhos do jardim de Ultima pela API HTTP. Use para pintar materiais, traçar trilhas ou elevar e rebaixar terreno.
---

# Terreno do jardim

Consulte o catálogo em `GET /v1/game/garden`. Com uma sessão do jogador,
envie `{"actions":[…]}` a `POST /v1/game/garden/terrain`.

- `paint` usa `x`, `z`, `r` e `mat`.
- `sculpt` usa `x`, `z`, `r` e `dh`, em metros.
- `path` usa `mat`, `r` e `points`, uma lista ordenada de pontos
  `{"x":0,"z":-14}`. A API interpola os pincéis.

O raio inteiro deve caber no jardim. Veja o
[contrato do jardim](../../../docs/USAGE-API-JARDIM.md) para limites e como
combinar famílias em uma alteração.
