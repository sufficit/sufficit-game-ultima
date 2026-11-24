# Publicação do backend do Ultima no Sufficit Run

## Investigação

O GitHub Pages já servia o cliente com suporte a corredores curvos. A API anterior não anunciava a capacidade. O deployment possuía montagem dedicada do PVC `world-data` em `/data`. O publicador padrão reconcilia o template inteiro, então a promoção foi limitada à referência da release.

## Alterações

- O contrato e o job de build do Run validaram e compilaram o diretório `app/` da main `298de77`, incluindo o código de `4646276`.
- Release `9bec3877aed1e0690a85e29381f79146dee05b5728ebc8b67db6e8738c5d9dba` promovida com a montagem antiga e troca sem sobreposição de processos que gravam o journal. A estratégia anterior foi restaurada depois da verificação.
- A primeira promoção fez rollback automático após detectar diferenças na representação das edições. O replay havia acrescentado apenas a sequência interna `op` a alguns registros. A comparação normalizada confirmou todas as edições antes da segunda promoção.

## Validação e entrega

- Deployment disponível e `/health` em estado `ok`. A API passou a anunciar `volumeTunnels`.
- Mesmo PVC vinculado. Na troca de versão, permaneceram a revisão 49, 32 edições, 2 tiles, 15 objetos e o catálogo existente.
- Duas sessões verificaram a criação do corredor e a recusa de undo pelo observador. O autor desfez o corredor, o observador confirmou a remoção e o journal avançou à revisão 51. O único GLB existente correspondeu ao SHA-256 do catálogo.
- O Chromium confirmou a ação **Corredor curvo** na Oficina do cliente publicado em estado conectado à API do Run.
- [Jogo](https://sufficit.github.io/sufficit-game-ultima/) · [Saúde da API](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/health) · [Mapa publicado](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/v1/game/map).

## Checkpoints da execução

1. [concluído] Terreno em chunks e câmera orbital. Comprovar streaming,
   descarte, névoa e colisão da câmera em cenário denso. Código e medições
   confirmados no GitHub Pages no build `7aede38`.
2. [concluído] Interface declarativa em Razor. Diálogo e Diário exercitados
   localmente e no build `c630ebb` com ações centralizadas e dois temas.
3. [concluído] Pipeline esquelético e multidões. 100/300 instâncias em dois
   tipos, textura compartilhada e LOD de 3.226 para 876 vértices. A publicação
   e a medição de regime estão no checkpoint 6.
4. [concluído] Notifies de animação. RulesSmoke comprovou loop, pausa,
   transições e fila. A mochila comprovou impacto e som nos dois modos
   no build de produção `f59bfde`.
5. [concluído] Oficina, undo, migração, catálogo por mundo, filtro
   SHA-256, limpeza de órfãos e gráficos avançados implementados e verificados
   localmente. Os corredores volumétricos passaram em dois navegadores com API
   local, o replay preservou o corredor após reiniciar e o undo convergiu.
   A API foi publicada e validada no Sufficit Run.
6. [concluído] Validar a entrega estática no GitHub Pages e converter os
   planos comprovados. [Evidências](202611232159-oficina-volumes-e-graficos.md).

Validação por entrega: publish Release, testes relevantes do repositório,
ensaio de navegador no build publicado e conferência do diff antes do commit.

O ensaio local reutilizou a câmera orbital e verificou recuo de 7,50 m para
1,25 m diante de pinheiros, recuperação do zoom, arrasto, roda e descarte do
chunk de origem nos dois renderizadores. A emenda e a mochila passaram.

A estrutura visual dos componentes de diálogo e Diário seguiu a hierarquia do Painel do jogo e os tokens da Sufficit.Blazor.UI. A pesquisa visual foi registrada na [atividade das janelas declarativas](202611232127-janelas-declarativas.md).
