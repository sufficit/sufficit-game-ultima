# Projeto Ultima

## Objetivo

Ultima é um jogo 3D de exploração em um mundo que o jogador pode modificar.
O jardim é o cenário inicial. A Guardiã apresenta o mundo, conversa com o
jogador e ajuda a alterar o cenário. O jogo é aberto e roda no navegador.
[Repositório](https://github.com/sufficit/sufficit-game-ultima) · [Jogar](https://sufficit.github.io/sufficit-game-ultima/)

## Público e plataforma

O protótipo público atende jogadores de navegador em desktop e celular.
A exploração funciona individualmente. Jogadores também podem entrar na
mesma sala por convite e conversar por texto ou voz.
[Guia do jogador](docs/USAGE-JOGO.md)

## Ciclo de jogo

O jogador percorre o terreno, examina objetos e conversa com personagens.
Pode coletar recursos para a mochila, ajustar o cenário pela Oficina do
mundo ou pedir alterações à Guardiã. Quando há uma sessão no mundo
compartilhado, a API valida e grava as edições para os outros jogadores.

## Marco atual

| Sistema | Disponível no protótipo |
| --- | --- |
| Exploração | Movimento, apoio dos pés, interação por proximidade e câmera orbital com estabilização vertical e recuo por colisão. |
| Mundo | Terreno explorável, coleta, mochila e edição de solo, vegetação e objetos. |
| Personagens | Conversas com histórico, inferência local da Guardiã por CPU ou GPU e notas de memória. |
| Ambiente | Ciclo de Sol e Lua com referência em Greenwich, hora local e clima selecionável. |
| Multiplayer | Salas por convite, chat e voz via WebRTC. O mundo compartilhado usa uma sessão na API. |

[Uso do mundo](docs/USAGE-MUNDO.md) · [API da Guardiã](docs/USAGE-API-JARDIM.md) ·
[Evolução da câmera](docs/activities/202611251845-indice-de-colisao-da-camera.md)

## Premissas e arquitetura

- O cliente usa C# e Blazor WebAssembly. Regras, terreno, simulação e câmera
  usam C#. O Babylon.js desenha a cena e acessa a geometria do renderizador.
- A interface usa Sufficit.Blazor.UI. A cena pode rodar em um worker, com
  fallback para a thread principal conforme o suporte do navegador.
- A API ASP.NET Core é opcional para iniciar o jogo. Quando conectada, é a
  autoridade do mapa compartilhado, das sessões e do catálogo do mundo.
- O código é público sob a licença MIT. O GitHub Pages publica o cliente e o
  Sufficit Run hospeda a API.

[Arquitetura](ARCHITECTURE.md) · [Design](DESIGN.md) · [Build e publicação](README.md) ·
[Licença](LICENSE)

## Histórico e trabalho em curso

O primeiro marco provou uma cena 3D navegável e o diálogo de proximidade.
O histórico técnico de cada incremento está no índice de documentação.
Os planos ativos incluem a evolução da Guardiã e a proposta de comportamento
para criaturas. [Atividades e planos](docs/README.md)
