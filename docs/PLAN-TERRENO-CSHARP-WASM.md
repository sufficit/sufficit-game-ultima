# Malha do terreno em C# WebAssembly

1. [concluído] Confirmar a integração do runtime com o worker e o modo principal.
2. [concluído] Migrar a geometria das escavações, preservar as emendas e validar os dois modos.
3. [em andamento] Publicar, verificar o jogo e registrar a atividade.

Escopo desta migração: alturas, escavações, amostras da paleta, vértices,
normais e triangulação. Conservar o Babylon como renderizador e a UI Blazor.
Prova do runtime executada no Chromium em contexto principal e worker.
Build Release, RulesSmoke e teste de emendas passaram no site local.
O Chromium encontrou 4.225 vértices no chunk inicial e 4.335 após a escavação
na divisa. A diferença de geometria e normais entre chunks foi zero nos dois
modos. O listener do worker usa `addEventListener` para que o carregador .NET
identifique corretamente o contexto de execução.

Próxima ação. Publicar na main e repetir os testes no GitHub Pages.
