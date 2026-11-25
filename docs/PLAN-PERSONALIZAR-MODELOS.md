# Personalização de modelos do jardim

Aceite: qualquer modelo colocável, inclusive GLB da Oficina, aceita
dimensões independentes e rotação ao criar ou personalizar uma instância.
O contrato em C# permite que cada modelo declare os próprios campos, valores
padrão e limites, e controle a validação dos ajustes. As instâncias conservam UID e posição, e a alteração chega ao mapa, à
cena, à colisão e ao replay. Propriedades específicas continuam no catálogo.

1. [concluído] Identificar a validação restrita à luminária e o ciclo do modelo na cena.
2. [concluído] Implementar a interface C# e aplicar parâmetros a modelos colocados.
3. [em andamento] Validar modelos distintos, replay e publicar cliente e servidor.

Release, RulesSmoke, API com GLB importado, replay e Chromium em worker e main aprovados. Duas samambaias do mesmo modelo mantiveram configurações distintas.

Próxima ação: publicar cliente e servidor e validar os contratos e a cena publicados.
