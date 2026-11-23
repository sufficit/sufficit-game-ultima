#!/usr/bin/env bash
# Módulo .NET WebAssembly carregado pelo worker de renderização.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
destination="${1:-$root/src/wwwroot/terrain-wasm}"
publish="$(mktemp -d)"
trap 'rm -rf -- "$publish"' EXIT
dotnet publish "$root/engine/Ultima.Terrain" -c Release -o "$publish" --nologo
mkdir -p "$destination/_framework"
cp -R "$publish/wwwroot/_framework/." "$destination/_framework/"
cp "$root/engine/Ultima.Terrain/host.js" "$destination/host.js"
