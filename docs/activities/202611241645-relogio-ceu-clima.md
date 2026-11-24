# Relógio GMT, céu de Greenwich e perfis de clima

## Investigação

O clima anterior alternava céu limpo e chuva. O sol tinha posição fixa.
O jogo precisava distinguir o instante usado pelo mundo do fuso usado
por cada jogador para ler a hora.

O painel do jogo existente e o `DESIGN.md` orientaram a integração. A
seção recolhível mantém o HUD livre. O campo de busca evita percorrer
centenas de fusos. A composição usa a Sufficit.Blazor.UI e a regra de
evitar cartões redundantes da referência anti-ai-slop da skill Refero.
[Direção visual](../../DESIGN.md)

## Alterações

- O mundo segue em tempo real o relógio UTC do equipamento, rotulado
  GMT no painel. O navegador detecta o fuso IANA automaticamente e
  aplica suas regras de horário de verão. O jogador pode buscar outra
  localidade e salvar a escolha no navegador.
- Sol e Lua são calculados em C# para Greenwich (51,4779° N, 0°). O
  Babylon.js aplica as direções, a iluminação, as sombras solares e
  a face lunar iluminada. O painel mostra a fase aproximada e a parte
  iluminada. O renderizador atualiza o céu a cada dez segundos.
  [Modelo orbital](https://stjarnhimlen.se/comp/ppcomp.html)
- Os perfis de céu limpo, nublado, neblina, chuva e chuva forte reúnem
  cobertura de nuvens, precipitação, densidade de neblina e vento.
  Nesta etapa, a seleção afeta a cena local e persiste no navegador.
  O catálogo pode ser usado futuramente pelo resolvedor por região do mapa.
  [Arquitetura](../../ARCHITECTURE.md#relógio-e-clima)
- A folha do menu móvel exibe o painel do jogo quando aberta.

## Validação e entrega

- As 167 verificações em C# passaram, incluindo solstícios, direção
  solar, noite e as quatro fases principais da Lua.
  [Almanaque da USNO](https://aa.usno.navy.mil/calculated/moon/phases?year=2026)
- O Chromium verificou a virada de data local, o horário de verão de
  Londres, a persistência e a restauração do fuso automático. Também
  verificou a Lua cheia noturna e as transições entre clima limpo,
  chuva forte e neblina.
- A regressão de emendas do terreno passou localmente nos dois modos.
- Build Release do motor e do cliente. O build `edd3ae602dc4ff714a56ba2cb4bc680a56f7b416` foi
  verificado no site publicado em worker (desktop) e thread principal
  (tela estreita).
  [Teste de relógio, céu e clima](../../tests/world-clock.e2e.cjs)
  · [Jogar Ultima](https://sufficit.github.io/sufficit-game-ultima/)
