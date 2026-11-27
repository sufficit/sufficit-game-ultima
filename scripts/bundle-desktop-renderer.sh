#!/usr/bin/env bash
set -euo pipefail

wwwroot="${1:?Expected a published wwwroot}"
shell="$wwwroot/js/game.js"
worker="$wwwroot/js/game-worker.js"
url="$(sed -n "s/.*BABYLON_CDN = '\([^']*\)'.*/\1/p" "$shell")"
sri="$(sed -n "s/.*BABYLON_SRI = 'sha384-\([^']*\)'.*/\1/p" "$shell")"
[[ "$url" =~ ^https://cdn\.jsdelivr\.net/npm/babylonjs@[0-9]+\.[0-9]+\.[0-9]+/babylon\.js$ ]]
[[ -n "$sri" ]]
grep -Fq "importScripts('$url')" "$worker"

mkdir -p "$wwwroot/js/vendor"
bundle="$wwwroot/js/vendor/babylon.js"
curl -fsSL --retry 2 --max-time 120 "$url" -o "$bundle"
actual="$(openssl dgst -sha384 -binary "$bundle" | openssl base64 -A)"
[[ "$actual" == "$sri" ]]
curl -fsSL --retry 2 --max-time 30 "${url%/babylon.js}/license.md" -o "$wwwroot/js/vendor/Babylon-LICENSE.md"
sed -i "s|$url|js/vendor/babylon.js|g" "$shell"
sed -i "s|$url|vendor/babylon.js|g" "$worker"
