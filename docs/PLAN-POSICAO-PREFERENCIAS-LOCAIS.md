# Posição e preferências locais

Aceite: recarregar ou voltar ao jogo no mesmo navegador recupera posição,
direção e preferências. Posições de servidores distintos ficam separadas.
Dados inválidos devolvem os padrões e a gravação evita o loop de quadros.

1. [concluído] Identificar preferências existentes, posição e pontos de entrada.
2. [concluído] Persistir a posição e completar as preferências locais.
3. [em andamento] Validar recarga nos dois renderizadores e conferir a publicação.

Validação local: Release, RulesSmoke, diálogo em worker e cenário de preferências em worker e main. O Chromium fechou a aba, abriu outra e conferiu pose, raio no motor, painel, contraste, modelo da CPU, servidores separados e pose inválida.

Próxima ação: conferir o build publicado e repetir o cenário de preferências.
