# Passo articulado sobre obstáculos baixos no Ultima

O jogador deve transpor automaticamente objetos com topo abaixo do joelho quando
avança contra eles. Cada pé sobe com flexão de coxa e joelho, e o corpo sobe e
desce conforme o apoio. Troncos em pé e obstáculos altos continuam sólidos.
A passagem funciona com o modelo carregado e com o corpo de fallback.

1. [concluído] Identificar o cálculo de colisão, a altura dos objetos e a estrutura das pernas.
2. [concluído] Implementar apoio e transposição, e animar as articulações do jogador.
3. [concluído] Conferir elevação, flexão, bloqueio alto, retomada do chão, regressões e Release.
4. [em andamento] Publicar na main, validar no GitHub Pages e converter em atividade.

A captura do mantenedor é a referência. O tronco caído precisa de um passo com
os pés em alturas diferentes. A mesma base sólida informa o limite do joelho e
a altura de apoio. Preservar o raio de colisão e a prioridade de diálogo.

O teste anterior ficava em z=1,1 m sem subir. Localmente, com o passo, a elevação do corpo foi de 0,08 para 0,39 m e um joelho flexionou 1,26 rad. O mesmo tronco em escala acima do joelho continuou sólido. O corpo inicial de fallback passou com a DLL atrasada e depois vestiu o modelo real. Publish Release, RulesSmoke, testes Node e ensaio Chromium worker e main passaram.

Próxima ação: publicar e repetir o ensaio no GitHub Pages.
