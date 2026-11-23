# Correção da renderização do terreno

## Investigação

O build `5a7fea4` reproduzia o céu sob a vegetação em WebGL2, apesar dos chunks
carregados. A grade manual avançava em +Z e reutilizava a ordem de índices do
GroundBuilder, cuja grade avança em −Z. A sondagem com `ComputeNormals` do Babylon
9.36.0 retornou uma normal −Y para a malha anterior. O descarte de faces traseiras
ocultava a superfície vista de cima. O material também usava uma cor média por
chunk, o que eliminava os contornos da trilha, e a repintura ainda buscava a
textura difusa anterior.

## Alterações

- Inverteu-se a ordem dos vértices de cada triângulo para que a face superior
  seja visível. As normais analíticas passaram a usar a distância correta entre
  as amostras.
- A textura emissiva guarda a paleta procedural e o grão por texel, com
  amostragem nas coordenadas do mundo e bordas compartilhadas entre chunks.
- Pintura e escultura atualizam a textura e os buffers do chunk. O descarte de
  chunks fora de alcance libera também seu material e sua textura.

## Validação e entrega

- `node --check src/wwwroot/js/game-core.js`, quatro testes Node, RulesSmoke,
  `git diff --check` e `dotnet publish src/Ultima.csproj -c Release` passaram. O
  publish emitiu os avisos existentes CS8601 e CS8625 em `Index.razor`.
- O commit `4260954` foi publicado na `main`. O build foi confirmado em
  `build.json` e na página do [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45220199125).
- No site publicado, Chromium exibiu grama e trilha em WebGL2 no modo padrão
  com worker na GPU AMD e no modo main com SwiftShader. Em ambos, pintura de neve
  mudou o texel central de `[158, 128, 87]` para `[244, 249, 254]` e a escultura de
  4 m elevou o vértice central de 0,05 m para 4,05 m. As edições ficaram na sessão
  de teste com API isolada. O navegador recorreu a WebGL2 na tentativa com WebGPU.
