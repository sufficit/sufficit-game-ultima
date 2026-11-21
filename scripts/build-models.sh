#!/usr/bin/env bash
# Compila cada pacote de modelos (models/Ultima.Models.*, exceto o Core) numa DLL própria,
# copia para <destino>/models/<Pacote>.bin e gera <destino>/models/models.json com SHA-256.
# Uso: scripts/build-models.sh [pasta wwwroot de destino]   (padrão: src/wwwroot)
# A extensão .bin evita que hospedagens estáticas bloqueiem arquivos .dll.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${1:-$ROOT/src/wwwroot}"
OUT="$DEST/models"
CORE_API=$(grep -oP 'ApiVersion = \K[0-9]+' "$ROOT/models/Ultima.Models.Core/ModelContracts.cs")
mkdir -p "$OUT"
entries=()
for proj in "$ROOT"/models/Ultima.Models.*/Ultima.Models.*.csproj; do
  name="$(basename "$proj" .csproj)"
  [ "$name" = "Ultima.Models.Core" ] && continue
  dotnet build "$proj" -c Release -o "/tmp/ultima-models/$name" --nologo -v q
  cp "/tmp/ultima-models/$name/$name.dll" "$OUT/$name.bin"
  sha=$(sha256sum "$OUT/$name.bin" | cut -d' ' -f1)
  entries+=("{\"file\":\"models/$name.bin\",\"sha256\":\"$sha\",\"coreApi\":$CORE_API}")
  echo "pacote $name sha256=$sha"
done
( IFS=,; echo "{\"coreApi\":$CORE_API,\"packages\":[${entries[*]}]}" ) > "$OUT/models.json"
echo "catálogo: $OUT/models.json"
