# Mochila e coleta no piloto do Ultima

A mochila contabiliza plantas, árvores, pedras, objetos plantados e materiais
da superfície. O modo Coletar permite mirar e clicar ou tocar num alvo próximo.
Alvos recolhidos desaparecem da cópia do cenário e a coleta do solo desbasta
uma porção. O estado persiste no navegador após recarregar. NPCs, jogadores e
elementos de interface ficam fora do conjunto de recursos.

1. [concluído] Identificar as instâncias de cenário, edição de altura e modelo de sessão.
2. [concluído] Ligar seleção e persistência da coleta à renderização, integrar Mochila na UI.
3. [concluído] Testar alvos distintos, alcance, duplicidade, recarga, worker e main e publish Release.
4. [em andamento] Publicar na main, verificar o GitHub Pages e converter em atividade.

Para este piloto, inventário e alteração visual são uma transação local. O
servidor atual usa sessões temporárias e não tem inventário persistido por
personagem. Coordenadas do mundo e identificadores estáveis das instâncias
preservam as coletas sem enviar comandos de remoção para o mapa compartilhado.
A captura e o Painel existente servem de referências para a UI. A entrada
principal será Mochila, com a ação Coletar e a contagem dos recursos.

Validação local: publish Release, coleta de toco e terra por clique e toque,
desbaste de 0,48 m, alcance, recarga, concorrência entre abas e regressão de
colisão e passo articulado, nos modos main e worker.

Próxima ação: publicar na main e repetir o teste no GitHub Pages.
