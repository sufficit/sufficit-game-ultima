---
name: ultima-garden-plants
description: Planta, substitui e remove vegetação colocada no jardim de Ultima por HTTP. Use para criar canteiros, fileiras de árvores ou composições com flores e samambaias.
---

# Plantas do jardim

Consulte `families.plants.assets` em `GET /v1/game/garden`. Envie
`{"actions":[…]}` a `POST /v1/game/garden/plants` com o token do jogador.

- Para plantar, use `{"action":"place","asset":"flora.fern","x":3,"z":-12}`.
  Pode incluir `scale` e `rot` em radianos.
- Para substituir, consulte `placements` em `GET /v1/game/map`. No mesmo
  lote, remova pelo `uid` e coloque a espécie nova no ponto desejado.
  Os objetos que surgem com o mapa não têm `uid` de colocação e ficam
  fora desta operação.

Use o [contrato do jardim](../../../docs/USAGE-API-JARDIM.md) para limites,
respostas e operações combinadas.
