# Documentação do Ultima

## Jogar

- [Guia do jogador](USAGE-JOGO.md) — controles, coleta, Guardiã e multiplayer.
- [NPC dialogue guide](USAGE-DIALOGUE.md) — conversation topics and how to extend them.
- [Environments and mission API guide](USAGE-ENVIRONMENTS.md).
- [Edições, relógio e clima](USAGE-MUNDO.md) — Oficina do mundo e tempo local.

## Contribuir

- [Visão do jogo](../PROJECT.md) — objetivo e estado atual do protótipo.
- [Design da interface](../DESIGN.md) e [arquitetura em C# e WebAssembly](../ARCHITECTURE.md).
- [API do jardim](USAGE-API-JARDIM.md), [janelas e diagnósticos](USAGE-JANELAS.md) e [animações](USAGE-ANIMACOES.md).
- [Characters API](USAGE-API-CHARACTERS.md) — read and update the owning character’s body profile.
- [Decisões históricas](../decisions/) — input, visual e API.

## Planos ativos (`PLAN-*.md`)

Cada frente de trabalho tem um plano próprio. Um plano permanece aberto
enquanto houver pendências; ao concluir o escopo inteiro ele é convertido em
[atividade](activities/) (veja o [AGENTS.md](../AGENTS.md)).

- [Shared room presence across environments](PLAN-SHARED-ENVIRONMENT-PRESENCE.md).
- [Genius local no Ultima](PLAN-GENIUS-LOCAL.md) — integração da Guardiã com sessões e inferência local.

As quatro frentes históricas estão registradas nas atividades de entrega.

- [OffscreenCanvas com fallback na thread principal](activities/202611220130-motor-worker-offscreen.md)
- [Cache OPFS de assets preparados](activities/202611212050-cache-opfs-preparados.md)
- [Decisões dos NPCs em C#](activities/202611220040-ia-npcs-decisoes.md)
- [Upload amortizado de modelos](activities/202611212030-upload-amortizado-modelos.md)

Futuros (bom ter na manga):

- [PLAN-MOB-BRAIN.md](PLAN-MOB-BRAIN.md) — IA determinística de criaturas
  (MobBrain): percepção, perseguição, ataque e retorno decididos sobre
  snapshot; proposta com o mapeamento do código concluído (checkpoint 1), ainda
  sem implementação.

Os registros preservam as referências técnicas do estudo do lineage-rust e
as medições de cada entrega.

## Atividades (`activities/`)

- [Characters API and real Genius inference](activities/202611261650-characters-api-and-genius-inference.md)
- [Ultima test suite repair](activities/202611261519-test-suite-repair.md)
- [File-size checks in the native test suites](activities/202611261458-native-file-size-tests.md)
- [Repository line limit and inline documentation](activities/202611261150-file-line-limit.md)
- [Independent humanoid proportions](activities/202611261105-humanoid-proportions.md)
- [Humanoid body profiles](activities/202611261057-humanoid-body-profiles.md)
- [Humanoid customization research](activities/202611261015-humanoid-parameter-research.md)
- [Modal windows keep the focus and isolate the game behind them](activities/202611260927-modal-window-focus-and-safe-area.md)
- [One engine for the garden and office](activities/202611260005-one-engine-environments.md)
- [Foot support during aimed locomotion](activities/202611252331-player-support-balance.md)
- [Expanded camera zoom and group framing](activities/202611252319-camera-zoom-and-group-framing.md)
- [Fine scroll adjustment for the close camera](activities/202611252304-close-camera-zoom.md)
- [Close third-person camera](activities/202611252119-close-camera.md)
- [Keyboard movement and camera look](activities/202611252057-keyboard-look.md)
- [Shared articulated player](activities/202611252051-shared-player-rig.md)

- [Camera perspectives in the garden and interiors](activities/202611252041-camera-perspectives.md)

- [Full-viewport office and third-person player view](activities/202611252033-interior-player-view.md)

- [Walkable office interior](activities/202611252024-walkable-office.md)

- [Hosted mission API rollout](activities/202611252005-mission-api-rollout.md)

- [Data-driven environments and missions](activities/202611251942-environment-missions.md)

- [Authored NPC dialogue topics](activities/202611251914-npc-dialogue.md)

- [Entrada de teclado, toque e gamepad](activities/202611251906-entrada-multidispositivo.md).

- [Atualização da documentação do Ultima](activities/202611251852-documentacao-do-jogo.md)

- [Índice espacial da câmera e medição de desempenho](activities/202611251845-indice-de-colisao-da-camera.md)

- [Estabilização vertical da câmera](activities/202611251826-estabilizacao-da-camera.md)

- [Plugin de memória da Guardiã no navegador](activities/202611251430-plugin-memoria-navegador.md)
- [Migração dos testes de mobile e de registro de conversas para C#](activities/202611251151-testes-mobile-e-dialogo-csharp.md)

- [Migração dos testes de integração do servidor para C#](activities/202611250333-testes-integracao-servidor-csharp.md)

- [Conversa com histórico e relatos de diagnóstico](activities/202611250122-conversa-e-diagnosticos.md)

- [Personalização de modelos por instância](activities/202611250007-personalizacao-modelos.md)

- [Luminárias paramétricas da Guardiã](activities/202611242347-luminarias-parametricas.md)

- [Posição e preferências locais](activities/202611242338-posicao-preferencias-locais.md)

- [Ação de iluminar o jardim](activities/202611242258-iluminar-jardim.md)

- [Reconhecimento da IA local pela Guardiã](activities/202611242244-reconhecimento-ia-guardia.md)

- [Controles de IA na conversa da Guardiã](activities/202611242216-guardia-controles-ia.md)

- [Migração do teste de serviços do navegador para C#](activities/202611242209-testes-servicos-navegador-csharp.md)

- [Migração dos testes de janelas e HUD para C#](activities/202611242159-testes-janelas-hud-csharp.md)

- [Migração dos testes de multidão e avatares para C#](activities/202611242149-testes-multidao-avatares-csharp.md)

- [Migração dos testes de túnel, histórico e gráficos para C#](activities/202611241834-testes-tunel-historico-graficos-csharp.md)

- [Guardiã e API do jardim](activities/202611241828-guardia-api-jardim.md)

- [Migração dos testes de câmera e terreno para C#](activities/202611241813-testes-camera-terreno-csharp.md)

- [Migração dos testes de passo, relógio e mochila para C#](activities/202611241803-testes-e2e-csharp.md)

- [Migração do teste de colisão do mundo para C#](activities/202611241746-colisao-e2e-csharp.md)

- [Sincronização horária do relógio GMT](activities/202611241733-sincronizacao-horaria.md)

- [Integração da virada à primeira passada](activities/202611241724-giro-e-passada-integrados.md)

- [Inversão do jogador no próprio eixo](activities/202611241713-giro-no-proprio-eixo.md)

- [Equilíbrio do jogador ao parar](activities/202611241654-equilibrio-ao-parar.md)

- [Relógio GMT, céu de Greenwich e perfis de clima](activities/202611241645-relogio-ceu-clima.md)

- [Estabilidade da postura em repouso](activities/202611241631-postura-em-repouso.md)

- [Alinhamento do joelho na inversão de marcha](activities/202611241621-joelho-na-inversao.md)

- [Resposta articulada ao contato](activities/202611241603-contatos-articulados.md)

- [Não sobreposição dos corpos durante a animação](activities/202611241300-nao-sobreposicao-dos-corpos.md)

- [Contato junto à silhueta](activities/202611241249-colisao-junto-as-malhas.md)

- [Contato corporal e tropeço](activities/202611241236-contato-de-personagens.md)

- [Alinhamento da marcha ao mudar de direção](activities/202611241221-alinhamento-da-marcha.md)

- [Progressão da passada e coordenação dos braços](activities/202611241150-progressao-da-passada.md)

- [Apoio mundial dos pés do jogador](activities/202611241138-apoio-mundial-dos-pes.md)

- [Marcha e base anatômica dos personagens](activities/202611241125-anatomia-e-marcha.md)

- [Visibilidade dos avatares remotos distantes](activities/202611241114-visibilidade-avatares-remotos.md)

- [Sombras dos avatares remotos por distância](activities/202611241105-sombras-avatares-remotos.md)

- [Desempenho dos avatares remotos em C#](activities/202611241052-desempenho-avatares-remotos.md)

- [Consultas volumétricas em C#](activities/202611241032-consultas-volumetricas-csharp.md)

- [Movimento de NPC e avatares remotos em C#](activities/202611241023-movimento-npc-remotos-csharp.md)

- [Preparação do cenário em C#](activities/202611240032-preparacao-cenario-csharp.md)

- [Auditoria dos planos e recuperação do cache de malhas](activities/202611240018-auditoria-planos-e-cache-do-terreno.md)

- [Paleta do terreno em C#](activities/202611240008-paleta-terreno-csharp.md)

- [Câmera e animação por quadro em C#](activities/202611232346-camera-animacao-csharp.md)

- [Simulação do jogador em C# no navegador](activities/202611232327-simulacao-csharp-navegador.md)

- [Publicação do backend no Sufficit Run](activities/202611232303-publicacao-do-backend-no-run.md)

- [Mundo expansível](activities/202611232303-mundo-expansivel.md)

- [Persistência do mundo](activities/202611232303-persistencia-do-mundo.md)

- [Catálogo de modelos do mundo](activities/202611232303-catalogo-de-modelos-do-mundo.md)

- [Oficina, corredores e perfil Cinemático](activities/202611232159-oficina-volumes-e-graficos.md)

- [Multidões esqueléticas](activities/202611232144-multidoes-esqueleticas.md)

- [Marcas da animação](activities/202611232144-marcas-da-animacao.md)

- [Diálogo e Diário em JSON](activities/202611232127-janelas-declarativas.md)

- [Câmera e streaming do terreno](activities/202611232122-camera-e-streaming.md)

- [Terreno em C# WebAssembly](activities/202611231906-terreno-csharp-wasm.md)

- [Curvas nas bordas das escavações](activities/202611231851-relevo-escavado-sem-v.md)

- [Emendas naturais no terreno](activities/202611231831-emendas-do-terreno.md)

- [Migração de serviços JavaScript para C#](activities/202611231825-migracao-js-para-csharp.md)

- [Movimento e expressão dos personagens](activities/202611231810-expressao-dos-personagens.md)

- [Destaque do alvo de coleta](activities/202611231755-mira-de-coleta.md)

- [Precisão dos cliques e escavação em camadas](activities/202611231736-precisao-e-camadas-da-coleta.md)

- [Apoio dos pés em troncos baixos](activities/202611231730-apoio-nos-troncos.md)

- [Mochila e coleta de recursos](activities/202611231722-mochila-e-coleta.md)

- [Passo articulado sobre obstáculos baixos](activities/202611231701-passo-articulado-sobre-obstaculos.md)

- [Colisão do jogador e inspeção de objetos](activities/202611231650-colisao-e-interacao-com-objetos.md)

- [Texturas de grama e terra, luz, vento e chuva](activities/202611231639-grama-terra-e-clima.md)

- [Oficina do mundo dentro do Painel do jogo](activities/202611231606-oficina-no-painel.md)

- [Estilos da Sufficit.Blazor.UI nos menus](activities/202611231448-estilos-sufficit-blazor-ui-nos-menus.md)

- [Correção do poço e acabamento do terreno](activities/202611231402-acabamento-do-terreno.md)

- [Correção da renderização do terreno](activities/202611231348-renderizacao-do-terreno.md)

- [Conflitos de movimento no harness mobile](activities/202611231306-conflitos-do-harness-mobile.md)

Documentos de atividade (`AAAAMMDDHHMM-slug.md`) registram frentes concluídas:
investigação, alterações e validação com evidências. São gravadas quando o
escopo inteiro de um plano é concluído.
