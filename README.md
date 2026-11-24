# Ultima

Jogo open source escrito em **C#**, rodando inteiramente no **navegador** via
**Blazor WebAssembly**, com mundo 3D renderizado pela GPU através do **Babylon.js**.
Sem servidor centralizado de jogo — tudo acontece no cliente.

- **Jogue agora:** https://sufficit.github.io/sufficit-game-ultima/ (GitHub Pages, deploy automático a cada push em `main`).
- **Licença:** MIT (veja [LICENSE](LICENSE)).
- **Marco atual (M1):** mundo 3D andável (WASD) e uma Guardiã com diálogo.
- **Marcos futuros:** multiplayer P2P (WebRTC DataChannels) e conversas do NPC com
  LLM executada localmente na GPU do jogador (WebGPU + WebLLM).

## Publicação (GitHub Pages)

O workflow [` .github/workflows/pages.yml`](.github/workflows/pages.yml) publica
automaticamente a cada push em `main`:

1. compila o módulo C# do terrain worker com `bash scripts/build-terrain.sh`;
2. executa `dotnet publish src/Ultima.csproj -c Release`;
3. ajusta o `base href` para `/sufficit-game-ultima/` (caminho do Pages);
4. gera `404.html` e `.nojekyll`;
5. publica o artefato em https://sufficit.github.io/sufficit-game-ultima/.

## API de conteúdo dinâmico (opcional)

A API pública em
`https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/` fornece conteúdo
atualizável sem republicar o jogo. O cliente busca as falas da Guardiã em
`/v1/npcs/guardia` e avisos em `/v1/announcements` depois que o mundo já começou
a renderizar. Cada resposta tem limite de três segundos; indisponibilidade,
timeout, CORS ou conteúdo inválido preserva as falas fixas embutidas e não impede
o jogo de funcionar. O HUD indica se o conteúdo veio da API ou do fallback local.

A API também expõe `/health`, `/v1/manifest`, `/v1/worlds/ultima` e `/v1/quests`.
O conteúdo editável fica em `app/content.json`; serviço implementado em Python
stdlib em `app/main.py`.

## Como rodar localmente

Requisitos:

- SDK do .NET 10 (`dotnet --version` deve mostrar 10.x);
- Workload `wasm-tools` para compilar o módulo do worker;
- Navegador com WebGL (qualquer navegador moderno).

Passos:

```bash
dotnet workload install wasm-tools
bash scripts/build-terrain.sh
cd src
dotnet run
```

O comando acima sobe o servidor de desenvolvimento do Blazor WASM e abre o jogo
(normalmente em `https://localhost:7xxx`; a URL exata é impressa no terminal).
Alternativa sem servidor de desenvolvimento:

```bash
bash scripts/build-terrain.sh
cd src
dotnet publish -c Release -o publish
python3 -m http.server -d publish/wwwroot 8080
# abra http://localhost:8080
```

> Nota: o Babylon.js é carregado de CDN com versão fixa e hash SRI
> (`src/wwwroot/index.html`), então a primeira execução precisa de internet.

## Controles

O parâmetro `?crowd=100` ou `?crowd=300` ativa a demonstração local de
multidões esqueléticas. A demonstração coloca dois tipos de personagem
na clareira para medir animação, LOD e culling.

| Tecla | Ação |
| --- | --- |
| `W` `A` `S` `D` ou setas | andar |
| `E` | conversar com a Guardiã próxima |
| `Esc` | fechar o diálogo |
| arrastar o mouse | girar a câmera |
| roda do mouse | aproximar / afastar |

## Estrutura

```
PROJECT.md          objetivos, premissas e marcos
LICENSE             MIT
.github/workflows/  deploy automático no GitHub Pages
app/                API Python e conteúdo remoto opcional
src/                projeto Blazor WebAssembly (Ultima)
  Pages/Index.razor HUD, diálogo e cliente opcional da API
  GAME.JsInterop.cs ponte C# ↔ JS (JSInvokable)
  wwwroot/js/game.js   Babylon.js: cena, câmera, movimento, gatilhos
  wwwroot/index.html   HTML raiz (Babylon.js com SRI)
  wwwroot/css/app.css  estilos do HUD e do diálogo
```

## Arquitetura em uma frase

C# cuida do estado do jogo e da UI; JavaScript é apenas a fina camada de
renderização 3D e entrada; a ponte usa JSInterop com chamadas unidirecionais
(`game.init`, `game.setFrozen`, `game.resetPlayer` de C#→JS; `OnEngineReady`,
`OnNearChanged`, `OnInteract`, `OnClose`, `OnMoveTick` de JS→C#). A API HTTP é
opcional e não faz parte do caminho crítico de inicialização do jogo.
