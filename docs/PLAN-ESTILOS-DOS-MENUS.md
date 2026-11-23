# Restaurar o estilo Sufficit.Blazor.UI nos menus

Critérios de aceite: a Oficina do mundo e o Painel do jogo carregam os estilos
isolados originais da Sufficit.Blazor.UI. Cabeçalhos ocupam a largura disponível,
com ícone, título e indicador alinhados. Seções abrem por clique e teclado e
continuam acessíveis em telas baixas. Manter o tema do Ultima e os fluxos do jogo.

1. [concluído] Reproduzir em produção e confirmar o bundle ausente.
2. [concluído] Carregar o bundle e testar aparência, teclado e rolagem.
3. [concluído] Validar o publish Release e os testes aplicáveis.
4. [em andamento] Publicar na main, validar no GitHub Pages e converter o plano.

Referências: captura do mantenedor, menu e tema existentes, estilos isolados e
integração HTML do Showcase da Sufficit.Blazor.UI. A Oficina e o Painel continuam
como entradas de configuração. Cada cabeçalho deve orientar a leitura pelo título
e usar o indicador e os estados de foco da biblioteca.

Reprodução em produção: nenhum CSS isolado solicitado. Cabeçalho de 160 px dentro de 306 px, com display inline-block e fundo cinza do navegador. O arquivo Ultima.styles.css já é publicado e importa o bundle com o seletor isolado da biblioteca.

Candidato: estilos prévios da biblioteca aplicados aos dois menus. Cabeçalho passou de 160 para 306 px no desktop. Todas as seções da Oficina abriram por Enter e fecharam pelo controle de abertura. Sem rolagem horizontal. Em 740 × 360, ancorar a coluna também pelo rodapé elevou a altura útil de 139 para 334 px.

Publish Release, RulesSmoke e git diff --check passaram. O index do artefato referencia Ultima.styles.css e seu import aponta para o arquivo isolado presente.

Próxima ação: publicar e repetir as medições com os arquivos servidos pelo Pages.
