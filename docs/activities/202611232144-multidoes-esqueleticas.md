# Multidões com animação esquelética na GPU

## Investigação

O corpo do jogador e dos NPCs já usava grupos de uma DLL validada por
SHA-256. Replicar o rig procedural por personagem multiplicaria nós,
atualizações de CPU e draws. As animações assadas do Babylon permitem
usar a mesma textura de matrizes para todas as instâncias de cada tipo.
[Documentação do Babylon](https://doc.babylonjs.com/features/featuresDeepDive/animation/baked_texture_animations).

## Alterações

- Aldeão e Andarilho fornecem o ciclo e suas matrizes como dados C#.
  Dois LODs compartilham esqueleto e textura por tipo. As fases variam
  por pessoa, e o efeito da locomoção permanece na GPU.
- O corte por alcance e frustum atualiza as instâncias visíveis. O LOD
  deriva da altura em pixels, com histerese e ajuste pelo FPS.
- A demonstração é ativada pelo parâmetro `crowd=100` ou `crowd=300`.
  [Formato de animações](../USAGE-ANIMACOES.md).

## Validação e entrega

- As DLLs e o cliente Release compilaram. O shader desenhou os dois
  tipos em WebGL 2, e o teste confirmou a textura e o esqueleto
  compartilhados pelos LODs. Cada malha caiu de 3.226 para 876 vértices.
- No ensaio de 300 instâncias em produção, 154 ficaram dentro do
  frustum inicial. Ao afastar a câmera, todas saíram da lista de draws.
  Após a carga da fila, seis amostras em três segundos ficaram em
  60 FPS no teste em 1.200 × 720 com GPU AMD via ANGLE. Essa medição
  caracteriza o equipamento testado.
- O build `efa3ce7` publicou a demonstração. A medição de regime foi
  repetida em `f59bfde`. [Teste](https://github.com/sufficit/sufficit-game-ultima/blob/865ca328adf67db9e5d172997ad05e4632eb6a20/tests/crowd.e2e.cjs)
  · [Jogo com multidão](https://sufficit.github.io/sufficit-game-ultima/?crowd=300)
  · [Publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45297298763).
