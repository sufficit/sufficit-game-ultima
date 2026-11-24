# Paleta e classificação do terreno em C# WebAssembly

## Investigação

O C# devolvia altura, inclinação e profundidade como três `Float64` por texel.
O JavaScript percorria essa grade para formar a paleta, as máscaras de detalhe
e o grão. A classificação usada na coleta repetia a mesma lógica de cor.

## Alterações

- O C# gera cor, grão, máscaras de grama e terra, relevo da rocha e o nome do
  material coletado. O Babylon.js continua compondo os detalhes WebP e enviando
  as texturas à GPU.
- A ponte leva 4 bytes por texel procedural ou 12 no modo detalhado, em planos
  RGBA lidos diretamente pelo canvas. Antes, o bloco de altura, inclinação e
  profundidade ocupava 24 bytes por texel.
- O gerador evita calcular a inclinação quando há neve saturada. Seleciona
  pinturas relevantes por chunk, inclui a margem do pincel na emenda e
  conserva a ordem do journal.
- O cache OPFS salva os segmentos RGBA e de relevo respeitando os limites
  das vistas do buffer.

## Validação e entrega

- Build Release de `Ultima.Terrain`, publish do cliente e 100 verificações
  em `RulesSmoke`.
- Nove hashes de pixels do Chromium coincidiram, com textura procedural,
  detalhada e após escavação e pintura. As emendas de cor, altura e normais
  coincidiram em worker e `?render=main`, com e sem WebP.
- O percurso da coleta e o armazenamento da textura em OPFS foram verificados
  no build final. Mochila e túneis também foram exercitados nos dois modos antes
  do filtro por chunk da pintura.
- Comparação com o build `2bb2efe` em Chromium com SwiftShader, viewport
  800×450, worker, perfil padrão e 25 itens por fase da fila de terreno.

| Medição de CPU | Mediana anterior | Mediana atual | p95 anterior | p95 atual |
| --- | ---: | ---: | ---: | ---: |
| Construção do chunk | 59,6 ms | 15,5 ms | 64,0 ms | 55,4 ms |
| Aplicação do detalhe WebP | 59,1 ms | 18,3 ms | 63,6 ms | 47,6 ms |

A medição representa o custo dessas tarefas na fila do renderer.
[Arquitetura](../../ARCHITECTURE.md) · [Workflow do cliente](../../.github/workflows/pages.yml)
