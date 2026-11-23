# Correção do poço e acabamento do terreno

## Investigação

A captura do mantenedor mostrava um disco cinza sobre a escavação perto da
clareira. A cópia do journal de produção confirmou o poço em (10, 10), com raio
de 5 m e piso em −2 m. No build `5693f1c`, um raio vertical acertava primeiro a
tampa sem textura de `carve-wall` a 0,30 m. Instâncias da vegetação também
ocupavam a área interna do poço.

A captura e a paleta do Ultima foram as referências visuais. A trilha mantém
tons de terra e a clareira mantém seu verde. O acabamento do poço usa o padrão
de rocha do cenário, com variações de escala para distinguir piso e paredes.

## Alterações

- O relevo do próprio terreno forma as paredes inclinadas e o fundo do poço.
  Salas totalmente abaixo do entorno conservam teto com textura de rocha.
- O solo recebe grão mais fino e os pedregulhos ganham estratos e variação de
  cor. O terreno escavado recebe textura de 256 × 256. Os demais chunks usam
  128 × 128.
- Os modelos de folhagem e de grama têm preenchimento leve nas sombras.
  O espalhamento automático oculta as instâncias cuja base cai dentro do poço.
  O replay restaura terreno, materiais e instâncias quando a edição sai do mapa.

## Validação e entrega

- `node --check`, os quatro testes Node, RulesSmoke, `git diff --check` e
  `dotnet publish src/Ultima.csproj -c Release` passaram.
- No build publicado `e854391`, Chromium com GPU AMD e WebGL2 repetiu o replay
  em worker e main. O piso e o vértice central ficaram em −2 m, a borda manteve
  0,67 m e a textura do chunk chegou a 256 px. No main, a captura mostrou o
  fundo e as paredes, o raio atingiu a malha do chão em −2 m e havia zero
  instâncias de cenário habilitadas dentro do raio.
- Uma sala de teste com piso em −6 m recebeu teto texturizado. Remover as
  edições por replay restaurou a altura original, a textura de 128 px e as
  instâncias naturais. Um novo evento de escavação recompôs o poço. O journal
  foi usado em sessão isolada e as operações de teste permaneceram na memória
  do navegador. A validação gráfica foi em WebGL2.
- Entrega no [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45222837361).
