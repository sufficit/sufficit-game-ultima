# Índice espacial da câmera e medição de desempenho

## Investigação

O perfil da órbita colocou a triagem das malhas entre os maiores custos
da consulta de colisão. Um protótipo que consultava C# por raio devolveu
as mesmas distâncias, mas a travessia da ponte WASM absorveu o ganho.

## Alterações

- A árvore de caixas em C# seleciona obstáculos por setor e alcance do zoom.
  A ponte consulta de novo quando muda o setor, aumenta o alcance ou muda
  a geometria. O Babylon mantém a interseção precisa com as malhas.
- A leitura da superfície reutiliza as vistas tipadas do bloco da malha.
  A política de estabilização e o cálculo de colisão volumétrica foram
  exercitados junto da órbita.
- O cenário C# `camera-performance` mede cinco lotes após aquecimento e
  compara as distâncias raio a raio entre builds.
  [Como repetir a medição](../../ARCHITECTURE.md#desempenho-da-migração)

## Validação e entrega

- Em Chromium com SwiftShader, a lista passou de 120 malhas para 23
  candidatas. O custo de um lote de 1.800 raios caiu de 9,9 ms para 6,6 ms
  na mediana. A etapa de malhas somou 26,6 ms antes e 5,8 ms depois nos
  cinco lotes. As 1.800 distâncias de colisão coincidiram.
- O build Release, RulesSmoke e os cenários de câmera, túnel, emendas e
  cache de terreno passaram. No Chromium do site publicado, o cenário de
  câmera confirmou a estabilidade vertical, o raio de 1,25 m contra
  pinheiros e o retorno à órbita em worker e na thread principal.
- Revisão publicada `478d0a3`. As medições dizem respeito à consulta
  isolada de colisão em software.
  [Jogo](https://sufficit.github.io/sufficit-game-ultima/) · [Deploy](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45706804504)
