# Ação de iluminar o jardim

Aceite: um clique instala duas luminárias e acende suas luzes na cena com uma
sessão conectada. Sem sessão, o diálogo indica como prosseguir. Repetir o clique
após salvar não duplica as luminárias.

1. [concluído] Rastrear o botão do diálogo até a API e o renderizador.
2. [concluído] Executar a ação pelo botão e testar a regressão no navegador.
3. [em andamento] Validar Release, publicar e conferir o GitHub Pages.

Validação local: Release e RulesSmoke passaram. O Chromium confirmou os dois pontos de luz acesos e a repetição sem duplicatas nos dois renderizadores, usando a API isolada. Sem sessão, o diálogo informa que precisa de conexão.

Próxima ação: validar o build publicado no GitHub Pages.
