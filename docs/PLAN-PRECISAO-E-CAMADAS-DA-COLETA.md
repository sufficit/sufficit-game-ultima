# Precisão dos cliques e escavação em camadas

Mirar no objeto visível ou no ponto do terreno, inclusive em telas de alta densidade. Permitir cavar várias vezes no mesmo ponto e conservar as coletas anteriores.

1. [concluído] Reproduzir o deslocamento do clique e a restrição de uma coleta.
2. [concluído] Corrigir as coordenadas e a escavação, incluindo a persistência e as regressões.
3. [em andamento] Publicar, testar em produção e registrar a atividade.

O build anterior falhou ao mirar no toco com DPR 2. O Babylon já ajusta as coordenadas pela escala do renderizador. A escavação era centralizada numa célula de 2 m com chave única.

Publish Release e regressão de colisão passaram localmente. O ensaio da mochila passou com clique e toque em DPR 2. Três cortes atingiram o mesmo ponto fora da grade e aprofundaram 1,32 m. Uma escavação legada pôde ser retomada após recarregar.

Próxima ação: publicar na main e repetir os ensaios no GitHub Pages.
