# Migração do JavaScript para C# no Ultima

1. [concluído] Inventariar os módulos e identificar contratos com o navegador e o worker.
2. [concluído] Migrar regras, estado, decisões e serviços portáveis e preservar os contratos persistidos.
3. [em andamento] Validar regressões, publicar e registrar a divisão final entre C# e JavaScript.

Migrar Oficina, mochila, convite, atualização e processamento dos registros
de diálogo. Preservar IndexedDB e contratos HTTP. O render, a entrada com
DOM, WebRTC, voz, runtime de IA e operações nativas do navegador permanecem
nos adaptadores JavaScript, evitando travessias por quadro entre o worker
de renderização e o Blazor.

Também migrar a sessão HTTP autoritativa, comandos e despacho de eventos.
Conservar o transporte P2P do navegador e a saída com keepalive.

Publish Release, RulesSmoke e testes de navegador passaram localmente, incluindo persistência da mochila, fila de diálogos, SHA-256, sessão, chat, mapa, edição, convite e atualização.

Próxima ação: publicar e repetir os testes no build servido pelo GitHub Pages.
