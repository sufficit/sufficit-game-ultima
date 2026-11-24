# Simulação do jogador em C# no navegador

## Investigação

O worker de renderização e o fallback na thread principal já compartilham o
módulo C# que calcula o terreno. A locomoção, a varredura de colisores, a
proximidade e a pose ainda eram calculadas no motor JavaScript. O gamepad tinha
duas implementações da zona morta. A prévia de coleta e o clique repetiam parte
da seleção.

## Alterações

- `PlayerMovement` resolve a zona morta radial, a caminhada e a corrida,
  a aceleração, o deslizamento em obstáculos, a passada baixa e a passagem
  por volumes. O worker e o fallback executam o mesmo código C#.
- O motor envia os colisores em lote quando o conjunto ativo muda. A consulta
  de proximidade usa esse mesmo conjunto. O Babylon.js consulta as malhas de
  apoio e calcula as interseções dos raios da câmera e da coleta.
- `PlayerPose` calcula a marcha, o gesto, a expressão facial e as rotações de
  quadril, joelho e tornozelo. A aplicação da pose e o despacho de eventos
  associados às marcas dos clipes permanecem no adaptador da cena.
- O destaque e o clique de coleta compartilham a resolução de alvo. A regra
  de alcance tridimensional é executada em C#.

## Validação e entrega

- Build Release de `Ultima.Terrain` e publish Release de `Ultima` concluídos.
  Os 89 casos do executável `RulesSmoke` passaram.
- Chromium com SwiftShader, em worker e `?render=main`: colisão com tronco
  alto e passagem sobre tronco baixo, apoio das solas, conversa, movimento de
  remotos, mochila, destaque e escavação em camadas, câmera, streaming e
  passagem por túnel.
- Entrada de gamepad simulada no Chromium nos dois modos. Entrada dentro da
  zona morta manteve o jogador parado. Entrada fora da zona avançou e a
  desconexão encerrou o deslocamento. A medição foi feita com renderização
  por software. Resultados de desempenho em GPU física exigem outra medição.
- O teste de túnel com espera fixa falhou também no build anterior publicado.
  O teste agora limita o tempo de caminhada e espera a distância esperada,
  com verificações de altura externa e interna preservadas.

A versão do cliente é verificada pelo SHA em `build.json` do GitHub Pages.
[Arquitetura](../../ARCHITECTURE.md) · [Workflow do cliente](../../.github/workflows/pages.yml)
