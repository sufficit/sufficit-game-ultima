# Pesquisa visual v3 — Ultima

## Brief
Jogo 3D low-poly/procedural para navegador, Babylon.js 9.36.0, WebGL2 e Blazor WASM. Meta: sair do aspecto de primitivas uniformes com materiais táteis, silhuetas legíveis e luz atmosférica, sem assets externos. Público: jogadores casuais. Restrição: a camada visual continua procedural e executa no cliente.

## Skill pesquisada
- **game-forge**, módulo `threejs-aaa-graphics-builder` (repositório público: https://github.com/efeislekel/game-forge-claude-skill/tree/main/skills/game-forge). Fornece scorecard de screenshots de gameplay, hierarquia de silhuetas, leitura de valores/cores e receitas de modelos/shaders. É escrito para Three.js, não para Babylon.js; foi usado apenas como referência de craft neutra quanto ao motor. A instalação não foi aprovada nem concluída.

## Fontes técnicas
- Babylon.js — materiais: https://doc.babylonjs.com/features/featuresDeepDive/materials/using/materials_introduction/
- Babylon.js — sombras: https://doc.babylonjs.com/features/featuresDeepDive/lights/shadows
- Babylon.js — Cascaded Shadow Maps: https://doc.babylonjs.com/features/featuresDeepDive/lights/shadows_csm
- Babylon.js — pipeline padrão: https://doc.babylonjs.com/features/featuresDeepDive/postProcesses/defaultRenderingPipeline
- Babylon.js — skybox: https://doc.babylonjs.com/features/featuresDeepDive/environment/skybox
- Babylon.js — partículas: https://doc.babylonjs.com/features/featuresDeepDive/particles/particle_system/particle_system_intro

## Síntese e ledger

| Decisão v3 | Base | Papel/limite | Motivo |
|---|---|---|---|
| Mapas de relevo derivados dos próprios canvas de casca e rocha | Babylon materials; arte procedural existente | Normal/bump apenas em materiais de casca e rocha | Luz revela sulcos sem assets externos; custo de uma amostra normal no shader por material |
| AO suave por cor de vértice em copas e pedras | Skill game-forge: technical-art / authoring recipes | Escurecimento limitado, não substituir sombra dinâmica | Ancorar objetos e quebrar aparência plana com custo quase nulo em runtime |
| PCF em uma luz direcional | Babylon shadows; manter direção de luz atual | Somente sombra da luz principal; sem CSM nesta cena pequena | Borda menos dura com um shadow map; evitar várias luzes/sistemas de sombra |
| Pipeline: MSAA 4x e bloom sutil | Babylon DefaultRenderingPipeline | Bloom contido, sem GlowLayer adicional | Suavizar serrilhado e realçar apenas brilhos, mantendo paleta natural |
| Partículas leves com textura radial gerada no canvas | Babylon ParticleSystem | Até 260 partículas, sem bitmap externo | Dar profundidade/vida sem adicionar geometria à cena |
| Humanóides com rosto, mãos, sapatos, cabelo e cápsulas mais subdivididas | Brief/skill visual: silhueta e legibilidade | Primitivas simples; não simular animação/articulação detalhada | Melhorar leitura de personagens mantendo pipeline procedural |
| Normalizar valores antes do interop C# | Evidência runtime: `getFps()` pode não ser finito durante inicialização | Passar posição/FPS finitos ao `OnMoveTick` | Evitar falha de desserialização JSON no primeiro frame |

## Validação e ressalvas
- `node --check` passou.
- Chromium headless com SwiftShader carregou Babylon.js 9.36.0/WebGL2: 585 meshes, 1 pipeline e 1 particle system; tecla W moveu de (0,0) a aproximadamente (0,0.65); console sem erros/avisos. Screenshot de prévia: `/tmp/shot-v3.png` (800×450).
- SwiftShader é renderização por software: FPS observado foi ~1–2 e não representa GPU física. O pipeline pode ser pesado em hardware fraco; medir numa GPU real antes de definir perfis finais.
- Não houve validação da conversa com E, câmera com mouse ou visual em GPU física. A habilidade Game Forge é centrada em Three.js e não foi instalada; a metodologia foi adaptada manualmente para Babylon.js.
- Esta pesquisa/patch não autoriza publicação externa.
