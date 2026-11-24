# Terreno do Ultima em C# WebAssembly

## Investigação

A [migração dos serviços do navegador](202611231825-migracao-js-para-csharp.md)
deixou a geometria do terreno no motor Babylon. O protótipo confirmou que o
mesmo código C# pode executar tanto no runtime do Blazor quanto dentro do
worker que renderiza em OffscreenCanvas.

O carregador do .NET usa a propriedade `onmessage` para identificar seu
worker auxiliar. A ponte do jogo foi registrada com `addEventListener`
para que o runtime seja iniciado como uma aplicação independente no
worker de renderização.
[Documentação do .NET em Web Workers](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-on-webworkers?view=aspnetcore-10.0).

## Alterações

- As alturas, inclinações, escavações sobrepostas, alterações de relevo
  e a malha adaptativa são calculadas em C# WebAssembly no navegador.
- A malha chega ao Babylon em um bloco binário por chunk. As amostras
  necessárias à paleta também são entregues em lote. O Babylon continua
  responsável pela renderização e o JavaScript compõe as texturas.
- O worker carrega seu próprio runtime do .NET. O modo de compatibilidade
  reutiliza o runtime do Blazor. O build do módulo do worker integra a
  publicação no GitHub Pages.

## Validação e entrega

- Publish Release, RulesSmoke, sintaxe JavaScript e Bash e `git diff --check`
  passaram.
- O [teste de emendas](https://github.com/sufficit/sufficit-game-ultima/blob/fdf37ed6cba91ec88e94a0cbc7b227182c1cf693/tests/terrain-seams.e2e.cjs) confirmou execução
  em C# no worker e no modo principal. A geometria e as normais coincidiram
  nas duas divisas medidas, incluindo após escavar. O chunk passou de
  4.225 para 4.335 vértices com o refinamento local. O worker passou
  também com as imagens dos materiais bloqueadas.
- O [teste de mochila](https://github.com/sufficit/sufficit-game-ultima/blob/a7609116e65b74cc35707c54669ada5a2c5576a2/tests/backpack.e2e.cjs) passou com escala 2×.
  A mira, a coleta de três camadas, a restauração após recarregar,
  o registro legado e a coleta concorrente entre abas foram verificados.
- Os quatro ensaios de navegador passaram no build `1dcb83e` em produção.
  [Jogo publicado](https://sufficit.github.io/sufficit-game-ultima/)
  · [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45275998176).
