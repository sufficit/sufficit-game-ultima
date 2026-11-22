#!/usr/bin/env bash
# Copia os assets GLB curados (assets/glb) para <destino>/assets e gera
# <destino>/assets/assets.json com o SHA-256 de cada arquivo — catálogo assinado
# consumido pelo módulo game.glb (js/game.js): bytes que não conferem são recusados.
# Uso: scripts/build-assets.sh [pasta wwwroot de destino]   (padrão: src/wwwroot)
# Fontes e licenças dos modelos: assets/LICENSE.md (CC0/Quaternius).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${1:-$ROOT/src/wwwroot}"
SRC="$ROOT/assets/glb"
OUT="$DEST/assets"
if [ ! -d "$SRC" ] || ! ls "$SRC"/*.glb >/dev/null 2>&1; then
  echo "assets/glb vazio ou ausente — nada a fazer" >&2
  exit 1
fi
mkdir -p "$OUT"
entries=()
for glb in "$SRC"/*.glb; do
  id="$(basename "$glb" .glb)"
  cp "$glb" "$OUT/$id.glb"
  sha=$(sha256sum "$OUT/$id.glb" | cut -d' ' -f1)
  entries+=("{\"id\":\"$id\",\"file\":\"assets/$id.glb\",\"sha256\":\"$sha\"}")
  echo "asset $id sha256=$sha"
done
( IFS=,; echo "{\"version\":1,\"assets\":[${entries[*]}]}" ) > "$OUT/assets.json"
echo "catálogo: $OUT/assets.json"
