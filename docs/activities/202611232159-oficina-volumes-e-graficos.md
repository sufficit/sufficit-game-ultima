# Oficina, corredores volumétricos e perfil Cinemático

## Investigação

O undo precisava remover a pincelada inteira sem sobrescrever as operações
posteriores de outros jogadores. A migração legada renomeava o arquivo
original antes de gravar a base. Grutas com altura única conseguiam formar
o piso, mas não representavam chão e teto na mesma coordenada. O worker
também descartava a preferência de PBR e a textura ambiente dependia do DOM.

## Alterações

- Lotes de superfície e undo append-only no journal. A Oficina pode
  reverter a última edição confirmada da sessão. A migração grava o snapshot
  antes de mover o arquivo antigo.
- Corredores curvos com marching cubes em C# WebAssembly. A grade em
  coordenadas de mundo fecha a malha na divisa dos chunks. Personagem e
  câmera consultam o volume na sua altura. A Oficina oferece a ferramenta
  quando a API anuncia suporte.
- Catálogo separado por instalação quando configurado, hashes de upload
  aprovados e limpeza opcional de GLBs órfãos. Guardiã e Andarilho oferecem
  variações procedurais mediante confirmação do jogador. A escolha usa
  parâmetros do WebLLM quando há produtor carregado, com proposta local.
- Perfil Cinemático com três cascatas, SSAO em meia resolução e foco no
  personagem. O cubemap procedural em pixels roda nos dois renderizadores.
- [Uso e configuração](../USAGE-MUNDO.md).

## Validação e entrega

- Release do backend e do cliente e RulesSmoke passaram. O
  [teste de integração](../../tests/world-store.integration.py) confirmou
  migração, job retomado, replay, dono do undo, catálogo segregado, aprovação
  por SHA-256 e limpeza de órfão sem tocar no arquivo catalogado.
- Em `105c2a7`, o [teste de gráficos](../../tests/graphics.e2e.cjs) confirmou
  os efeitos na GPU AMD com WebGL 2, e o [teste de janelas](../../tests/declarative-windows.e2e.cjs)
  confirmou a proposta local e a aplicação por clique.
  [Workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45299630450).
- Em `4646276`, [corredor](../../tests/tunnel.e2e.cjs) passou em worker e
  modo principal. A superfície manteve aproximadamente 0,51 m, e a
  caminhada subterrânea ficou em aproximadamente −4,73 m no trecho
  medido. O [teste das emendas](../../tests/terrain-seams.e2e.cjs)
  também passou na publicação.
- O [ensaio de dois navegadores](../../tests/world-undo.e2e.cjs) criou
  e reverteu pincel e corredor com o cliente publicado e um backend
  descartável. A ligação ao loopback ocorreu por rota do Playwright
  apenas no ensaio. [Jogo](https://sufficit.github.io/sufficit-game-ultima/)
  · [Workflow](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45300996367).
- A API foi publicada no Sufficit Run. [Validação em produção](202611232303-publicacao-do-backend-no-run.md).
