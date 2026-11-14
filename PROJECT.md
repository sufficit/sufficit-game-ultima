# Projeto: Ultima (sufficit-game-ultima)

Nome do jogo: **Ultima** (latim). Repositório: https://github.com/sufficit/sufficit-game-ultima

## Objetivo
Protótipo de jogo 3D escrito em C#, open source, que roda inteiramente no navegador
(Blazor WebAssembly), com renderização 3D acelerada por GPU (Babylon.js via WebGL/WebGPU).
Sem servidor centralizado: tudo no cliente. Em um marco futuro, as conversas dos personagens
usarão LLM executada na própria placa de vídeo do jogador (WebGPU + WebLLM), e o multiplayer
será P2P (WebRTC), também sem servidor de jogo.

## Público
Jogador individual, em navegador desktop (prévia local privada). Sem custo de infraestrutura.

## Premissas
- C# / .NET 10, Blazor WebAssembly Standalone.
- Babylon.js fixado por versão e carregado com SRI (subresource integrity); renderização
  acelerada por GPU pela engine (`engine.getGlInfo().renderer` para comprovar).
- Nenhum servidor de aplicação no marco atual; HTML estático.
- Multiplayer P2P e LLM local via WebGPU são marcos futuros, fora da fatia atual.
- Open source: código-fonte, licença MIT e instruções de build no próprio repositório.

## Arquitetura simples
- `src/` — projeto Blazor WASM (`Ultima`), UI em componentes Razor.
- `src/wwwroot/js/game.js` — camada de renderização: Babylon.js, câmera, luzes, chão,
  personagem, NPC; movimento WASD por teclado com loop `requestAnimationFrame` em JS puro.
- `src/wwwroot/index.html` — carrega Babylon.js (versão fixa + SRI) e `game.js`.
- C# (Blazor) é a casca: HUD, diálogo com o NPC, status do motor. O estado de diálogo
  (falas fixas) mora em C#, a ponte com o JS é via JSInterop.
- `app/` — API de conteúdo dinâmico opcional (Python stdlib); o jogo é Blazor WASM puro e funciona sem ela.

## Destino de publicação
- Código: repositório público **sufficit/sufficit-game-ultima** (org Sufficit), sempre sincronizado a cada alteração.
- Jogo: GitHub Pages via Actions (`.github/workflows/pages.yml`), https://sufficit.github.io/sufficit-game-ultima/.
- API de conteúdo (`app/`, Python stdlib, publicada como app Sufficit): opcional; o jogo nunca depende dela e usa fallback embutido. Fornece orientações de NPC, missões, mundo/tempo e avisos em tempo real.

## Marco atual (M1)
Mundo 3D mínimo jogável no navegador:
1. Cena Babylon.js com chão, luzes e névoa leve, GPU confirmada.
2. Personagem (esfera) controlado por WASD/setas, com limites do mundo.
3. NPC (cap sul) com diálogo de falas fixas acionado por proximidade + tecla E.
4. HUD em Blazor (C#): título, instruções, painel de diálogo, status do motor.

## Critérios de aceite do M1
- `dotnet publish -c Release` determinístico concluído sem erro.
- Servir a pasta publicada e carregar em navegador real:
  - canvas 3D renderiza (chão, personagem, NPC visíveis);
  - `engine.getGlInfo().renderer` retorna o adaptador de vídeo real (GPU);
  - WASD move o personagem no mundo;
  - tecla E abre o diálogo do NPC com pelo menos 3 falas em sequência, tudo pelo estado C#.
- Checkpoint Git criado somente após a validação acima; workspace final sem mudanças pendentes.

## Marcos futuros (fora do escopo atual)
- M2: multiplayer P2P com WebRTC DataChannel (sem servidor centralizado; sinalização mínima).
- M3: LLM local via WebGPU (WebLLM) para as conversas dos personagens, com fallback
  determinístico nos navegadores sem suporte.
- M4: conteúdo, arte e ciclo de jogo; publicação como app estático aberto.

## Notas de implementação
- SDK instalado: .NET 10.0.404; templates `blazorwasm` disponíveis; pacotes 10.0.15.
- O jogo vai ao GitHub Pages; só a API de conteúdo (`app/`) é publicada como app Sufficit (`python`).
