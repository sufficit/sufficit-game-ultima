#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
output="$(realpath -m "${1:-$root/publish/desktop}")"
mkdir -p "$output"
stage="$(mktemp -d -p "$output" .stage.XXXXXX)"
trap 'rm -r -- "$stage"' EXIT
head="$(git rev-parse HEAD)"
build="$(git rev-parse --short=12 HEAD)"

bash scripts/build-models.sh
bash scripts/build-assets.sh
bash scripts/build-terrain.sh "$stage/terrain-wasm"
dotnet publish src/Ultima.csproj -c Release -o "$stage/web" --nologo
web="$stage/web/wwwroot"
if [[ -d "$web/terrain-wasm" ]]; then
    rm -r -- "$web/terrain-wasm"
fi
mv "$stage/terrain-wasm" "$web/terrain-wasm"
sed -i "s/__BUILD__/$head/g" "$web/index.html"
printf '{"build":"%s"}\n' "$head" > "$web/build.json"
bash scripts/bundle-desktop-renderer.sh "$web"

for runtime in linux-x64 win-x64; do
    name="Ultima-$runtime-$build"
    package="$stage/$name"
    dotnet publish desktop/Ultima.Desktop.csproj -c Release -r "$runtime" --self-contained true \
        -o "$package" --nologo
    cp -a "$web" "$package/wwwroot"
    cp LICENSE "$package/LICENSE"
    cp assets/LICENSE.md "$package/ASSETS-LICENSE.md"
    cp docs/USAGE-DESKTOP.md "$package/README.md"
    if [[ "$runtime" == linux-x64 ]]; then
        tar -C "$stage" -czf "$stage/$name.tar.gz" "$name"
        archive="$name.tar.gz"
    else
        (cd "$stage" && zip -q -r "$name.zip" "$name")
        archive="$name.zip"
    fi
    mv "$stage/$archive" "$output/$archive"
    (cd "$output" && sha256sum "$archive" > "$archive.sha256")
    printf 'Package: %s/%s\n' "$output" "$archive"
done
