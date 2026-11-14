# Ultima

Jogo open source escrito em **C#**, rodando inteiramente no **navegador** via
**Blazor WebAssembly**, com mundo 3D renderizado pela GPU através do **Babylon.js**.
Sem servidor centralizado de jogo — tudo acontece no cliente.

- **Jogue agora:** https://sufficit.github.io/sufficit-game-ultima/ (GitHub Pages, deploy automático a cada push em `main`).
- **Licença:** MIT (veja [LICENSE](LICENSE)).
- **Marco atual (M1):** mundo 3D andável (WASD) e um NPC com diálogo de falas fixas.
- **Marcos futuros:** multiplayer P2P (WebRTC DataChannels) e conversas do NPC com
  LLM executada localmente na GPU do jogador (WebGPU + WebLLM).

## Publicação (GitHub Pages)

O workflow [`.github/workflows/pages.yml`](.github/workflows/pages.yml) publica
automaticamente a cada push em `main`:

1. `dotnet publish src/Ultima.csproj -c Release`;
2. ajusta o `base href` para `/sufficit-game-ultima/` (caminho do Pages);
3. gera `404.html` e `.nojekyll` (arquivos `.br`/`.gz` do Blazor passam intactos);
4. publica o artefato em https://sufficit.github.io/sufficit-game-ultima/.

## API de conteúdo dinâmico (planejada — o jogo funciona sem ela)

O jogo **não depende** da API para funcionar; ela é usada em eventos específicos
para injetar conteúdo atualizável em tempo real, sem publicar uma nova versão do
jogo: orientações e falas dos NPCs, missões, detalhes de mapa, clima/tempo do
mundo e avisos da equipe. Enquanto a API está offline ou inacessível, o jogo usa
o conteúdo embutido (fallback determinístico). O contrato REST será documentado
em `docs/api.md` e o contrato de dados fica versionado no próprio repositório.

## Como rodar localmente

Requisitos:

- SDK do .NET 10 (`dotnet --version` deve mostrar 10.x);
- Navegador com WebGL (qualquer navegador moderno).

Passos:

```bash
cd src
dotnet run
```

O comando acima sobe o servidor de desenvolvimento do Blazor WASM e abre o jogo
(normalmente em `https://localhost:7xxx`; a URL exata é impressa no terminal).
Alternativa sem servidor de desenvolvimento:

```bash
cd src
dotnet publish -c Release -o publish
python3 -m http.server -d publish/wwwroot 8080
# abra http://localhost:8080
```

> Nota: o Babylon.js é carregado de CDN com versão fixa e hash SRI
> (`src/wwwroot/index.html`), então a primeira execução precisa de internet.

## Controles

| Tecla | Ação |
| --- | --- |
| `W` `A` `S` `D` ou setas | andar |
| `E` | conversar com o NPC próximo |
| `Esc` | fechar o diálogo |
| arrastar o mouse | girar a câmera |
| roda do mouse | aproximar / afastar |

## Estrutura

```
PROJECT.md          objetivos, premissas e marcos
LICENSE             MIT
.github/workflows/  deploy automático no GitHub Pages
src/                projeto Blazor WebAssembly (Ultima)
  Pages/Index.razor HUD e diálogo em C#
  GAME.JsInterop.cs ponte C# ⇄ JS (JSInvokable)
  wwwroot/js/game.js   Babylon.js: cena, câmera, movimento, gatilhos
  wwwroot/index.html   HTML raiz (Babylon.js com SRI)
  wwwroot/css/app.css  estilos do HUD e do diálogo
```

## Arquitetura em uma frase

C# cuida do estado do jogo e da UI; JavaScript é apenas a fina camada de
renderização 3D e entrada; a ponte usa JSInterop com chamadas unidirecionais
(`game.init`, `game.setFrozen`, `game.resetPlayer` de C#→JS; `OnEngineReady`,
`OnNearChanged`, `OnInteract`, `OnClose`, `OnMoveTick` de JS→C#).
