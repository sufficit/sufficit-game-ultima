# Licenças e proveniência dos assets 3D

## assets/glb/*.glb — modelos low-poly CC0

Todos os modelos abaixo são **CC0 (domínio público)**, do autor **Quaternius**
(quaternius.com), obtidos via [Poly Pizza](https://poly.pizza) (espelho por modelo,
com licença declarada na página). Uso comercial permitido, sem atribuição exigida;
creditamos mesmo assim por cortesia.

| Arquivo | Modelo original | Página de origem |
|---|---|---|
| `glb/scenery.bush.glb` | Bush with Berries | https://poly.pizza/m/TSbIxkDtxF |
| `glb/flora.flowers.glb` | Flowers (7 tufos) | https://poly.pizza/m/NBUxHir6FJ |
| `glb/flora.mushroom.glb` | Mushroom | https://poly.pizza/m/db1tjMhmiA |
| `glb/flora.fern.glb` | Fern | https://poly.pizza/m/jqcanvH7D6 |
| `glb/prop.log.glb` | Wood Log with Moss | https://poly.pizza/m/nwsYvcI0bC |
| `glb/prop.stump.glb` | Tree Stump with Moss | https://poly.pizza/m/nFvEbUX6LE |
| `glb/flora.pine.glb` | Pine (alternativa low-poly) | https://poly.pizza/m/79gmlLnweB |
| `glb/scenery.grass2.glb` | Grass (alternativa low-poly) | https://poly.pizza/m/Db4UVcNWnF |
| `glb/rock.boulder2.glb` | Rock Large (alternativa low-poly) | https://poly.pizza/m/d2VWOdthtR |

Pacote de origem: *Stylized Nature MegaKit* / *Ultimate Stylized Nature Pack*
(quaternius.com, licença CC0 anunciada na página do pack).

O catálogo servido (`src/wwwroot/assets/assets.json`) é gerado por
`scripts/build-assets.sh` com o SHA-256 de cada GLB — o cliente recusa bytes
que não conferem com o hash do catálogo.

## src/wwwroot/js/vendor/babylonjs.loaders-9.36.0.min.js

Cópia vendorizada (pinada) do loader glTF oficial do Babylon.js, versão 9.36.0
(a mesma do core carregado via CDN no `index.html`), baixada de
`https://cdn.babylonjs.com/v9.36.0/loaders/babylonjs.loaders.min.js`.

- Licença do Babylon.js: **Apache License 2.0** (https://www.apache.org/licenses/LICENSE-2.0).
- SHA-256: `c20deb85772d2f7ba0b70ccae74a79a6fe0b0d6b7eaba4721154a808929cfee7`
- SRI (sha384, aplicado na injeção em `js/game.js`):
  `OXd9U4rMASknPaxNfNxZEGFw9NDqAW2RISOw9OBdTcMureZGhfxRzaKspEgn8D+L`

Vendorizar (em vez de CDN sem versão) garante que o arquivo é exatamente este,
auditável no git, e continua disponível mesmo se o CDN mudar.
