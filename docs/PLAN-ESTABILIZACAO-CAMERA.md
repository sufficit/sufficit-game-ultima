# Estabilização vertical da câmera

1. [concluído] Usar a superfície de apoio como referência da câmera,
   amortecer as mudanças de altura em C# e recentrar em teletransportes.
2. [concluído] Validar em Release e no navegador as passadas, o desnível e
   a colisão da câmera. Usar o mesmo alvo para a órbita e para a colisão.
3. [em andamento] Publicar e validar a revisão no GitHub Pages.

A altura do quadril varia com a pose dos pés. A câmera deve seguir o piso
e manter X/Z responsivos. O filtro deve funcionar no worker e na main
thread, acompanhar desníveis e preservar a órbita, o zoom e o recuo.

O cenário falhou na revisão anterior em produção, com 0,5399 m acumulados
tanto no corpo quanto na câmera. A revisão candidata passou localmente
em worker e main. Também passaram o publish Release e RulesSmoke.
