# Ultima

Jogo de exploração 3D de código aberto. O cliente executa C# no navegador
com **Blazor WebAssembly** e renderiza o mundo com **Babylon.js**. A API
em **ASP.NET Core** oferece sessões e persistência no mundo compartilhado.

- [Jogar no GitHub Pages](https://sufficit.github.io/sufficit-game-ultima/)
- [Design e uso da Sufficit.Blazor.UI](DESIGN.md)
- [Arquitetura e evolução do C# no cliente](ARCHITECTURE.md)
- [Licença MIT](LICENSE)

## Publicação

O GitHub Pages publica o cliente a cada push em `main`. O workflow compila
os modelos e o terreno, prepara o catálogo de assets e publica o Blazor
WebAssembly. A API tem implantação própria no Sufficit Run.
[Workflow do GitHub Pages](.github/workflows/pages.yml) · [Configuração da API](app.json)

## API e conteúdo do mundo

A API fornece o mapa compartilhado, comandos, catálogo de assets e conteúdo
atualizável, como falas da Guardiã e avisos. O jogo inicia sem a API e usa o
conteúdo embutido quando o conteúdo remoto está indisponível. Use `?live=0`
para desativar a conexão com o mundo compartilhado.
[Uso do mundo](docs/USAGE-MUNDO.md) ·
[Saúde da API](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/health)

## Como rodar localmente

Requisitos:

- SDK do .NET 10 (`dotnet --version` deve mostrar 10.x);
- Workload `wasm-tools` para compilar o módulo do worker;
- Navegador com WebGL (qualquer navegador moderno).

Passos:

```bash
dotnet workload install wasm-tools
bash scripts/build-models.sh
bash scripts/build-assets.sh
bash scripts/build-terrain.sh
cd src
dotnet run
```

O comando acima sobe o servidor de desenvolvimento do Blazor WASM e abre o jogo
(normalmente em `https://localhost:7xxx`; a URL exata é impressa no terminal).
Com os artefatos já preparados, para servir o build estático execute na raiz
do repositório:

```bash
dotnet publish src/Ultima.csproj -c Release -o publish
python3 -m http.server -d publish/wwwroot 8080
# abra http://localhost:8080
```

O Babylon.js é carregado de CDN com versão fixada. O fallback na thread
principal verifica a integridade do script com SRI.
[Inicialização do cliente](src/wwwroot/js/game.js)

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

| Diretório | Conteúdo |
| --- | --- |
| [src/](src) | Cliente Blazor WebAssembly, interface e integração com Babylon.js |
| [engine/Ultima.Terrain/](engine/Ultima.Terrain) | Geração de terreno em C# para WebAssembly |
| [models/](models) | Pacotes de modelos 3D em C# |
| [app/](app) | API em ASP.NET Core e conteúdo remoto |
| [tests/](tests) | Testes de regras, integração e navegador |
| [docs/](docs/README.md) | Guias, planos e histórico de atividades |
