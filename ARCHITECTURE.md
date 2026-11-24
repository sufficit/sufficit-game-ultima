# Arquitetura do Ultima

O cliente usa Blazor WebAssembly no .NET 10. C# executa na máquina do jogador e
já responde pela interface, por regras, pelos modelos e pela geração de terreno.
Babylon.js desenha a cena pela GPU e ainda executa parte da lógica de movimento,
câmera e seleção. O backend ASP.NET Core é opcional para iniciar o jogo e atua
como autoridade do mundo compartilhado quando conectado.

```mermaid
flowchart LR
    UI["Blazor WASM · C# e Sufficit.Blazor.UI"]
    DOM["JavaScript · entrada e APIs do navegador"]
    Scene["Babylon.js · cena e renderização"]
    Terrain["WebAssembly · terreno em C#"]
    API["ASP.NET Core · mundo compartilhado"]
    UI <-->|JS interop| DOM
    DOM <-->|Comandos e eventos| Scene
    Scene <-->|Dados em lote| Terrain
    UI <-->|HTTP| API
```

## Responsabilidades atuais

| Camada | Responsabilidade |
| --- | --- |
| [Cliente Blazor](src/Ultima.csproj) | Estado dos controles, menus, janelas e ações de interface. Os componentes vêm da Sufficit.Blazor.UI, com o tema do jogo descrito em [DESIGN.md](DESIGN.md). |
| [Regras](src/Rules) e [NPCs](src/Npc) | Regras de interação e diálogo, agendamento de NPCs e eventos de animação. São código do jogo executado em C#. |
| [Serviços do navegador](src/Browser) | Mochila, Oficina, diário de diálogos, atualizações e sessão do mundo compartilhado. C# coordena os fluxos. Adaptadores JavaScript acessam APIs como IndexedDB e localStorage. |
| [Ponte de entrada e cena](src/wwwroot/js/game.js) | Captura entrada do navegador e encaminha comandos. [GAME.JsInterop.cs](src/GAME.JsInterop.cs) recebe eventos do motor para o Blazor. [game-core.js](src/wwwroot/js/game-core.js) mantém a cena e executa materiais, câmera, movimento e seleção. |
| [Terreno em C#](engine/Ultima.Terrain) | Calcula altura, superfície, malhas e escavação volumétrica. [BrowserExports.cs](engine/Ultima.Terrain/BrowserExports.cs) expõe operações que entregam dados de geometria em lote para a cena. |
| [Modelos em C#](models) | Pacotes de personagens, árvores, rochas e cenário. O [carregador](src/Models/ModelLoader.cs) confere SHA-256 antes de carregar cada pacote e usa marcadores quando o pacote é recusado. |
| [Servidor](app/UltimaServer.csproj) | Sessões, eventos, validação de comandos e edições, mapa compartilhado e catálogo de assets. A API atual está em `app/`. O diretório [app-python](app-python) guarda a implementação anterior. |

## Modos de execução e persistência

- **Worker.** Quando o navegador oferece suporte, `game-worker.js` executa a
  cena com `OffscreenCanvas` e inicializa o módulo .NET de terreno. O Blazor e
  os eventos do DOM ficam na thread principal.
  [Inicialização do worker](src/wwwroot/js/game-worker.js)
- **Thread principal.** `?render=main` permite exercitar o fallback. A cena roda
  na thread principal e usa o código de terreno compartilhado com o projeto
  Blazor. Mudanças de terreno, movimento e interação precisam preservar o
  comportamento nos dois modos.
  [Inicialização do cliente](src/wwwroot/js/game.js) · [Projeto Blazor](src/Ultima.csproj)
- **Mundo compartilhado.** O cliente busca o mapa em `/v1/game/map` e envia
  comandos a `/v1/game/command`. O servidor valida ações e persiste as edições
  com log e snapshot. A implantação monta o volume `world-data` em `/data`.
  `?live=0` desativa a conexão com o mundo compartilhado. A mochila fica
  armazenada no navegador.
  [Uso do mundo](docs/USAGE-MUNDO.md) · [Persistência](app/WorldStore.cs) ·
  [Deployment do backend](docs/activities/202611232303-publicacao-do-backend-no-run.md)

## Evolução do C# no cliente

1. **Extrair cálculos de movimento e seleção.** Coloque em C# as partes
   determinísticas de velocidade, aceleração, apoio, transposição de objetos e
   escolha do alvo de coleta. Preserve no Babylon.js as consultas espaciais à
   cena e envie seus resultados em estruturas compactas. Use a mesma decisão de
   alvo para o destaque do mouse e a ação de coleta.
2. **Estabilizar os contratos de interoperabilidade.** Declare os dados que
   cruzam a ponte em tipos C# e documente unidades, coordenadas e formatos de
   buffers. Agrupe comandos, eventos e resultados. Preserve a leitura de mapas
   e dados persistidos existentes ao evoluir o contrato.
3. **Levar regras de ação e animação para o mesmo estado de jogo.** Use C# para
   decidir ações e transições, reaproveitando os marcadores de animação
   existentes. O Babylon.js aplica as poses e mantém os recursos de GPU.
   DOM, Web Audio, WebRTC e armazenamento do navegador continuam atrás dos
   adaptadores que operam essas APIs.
   [Contrato de animações](docs/USAGE-ANIMACOES.md)

Para cada extração, compare o comportamento nos dois modos e meça o custo da
ponte, o tempo de frame e as alocações. Envie terreno e poses em lote e mantenha
as atualizações do HUD vinculadas ao estado de que a interface precisa. Cubra as
regras com [RulesSmoke](tests/RulesSmoke) e verifique câmera, colisão, coleta e
terreno pelos [testes de navegador](tests) correspondentes.

## Publicação

O GitHub Pages publica o cliente estático a partir de `main`. O workflow
compila os pacotes de modelos e o terreno, prepara o catálogo de assets e
publica o Blazor WebAssembly. O backend tem implantação própria no Sufficit
Run. Verifique `build.json` do cliente para confirmar o SHA servido e `/health`
da API após uma implantação do backend.
[Workflow do cliente](.github/workflows/pages.yml) · [Configuração do Run](app.json)
