# Mover a Oficina do mundo para o Painel

Aceite: com o Painel fechado, a Oficina não aparece sobre o mapa. O caminho
Painel → Oficina do mundo revela as ferramentas atuais com Sufficit.Blazor.UI.
A gaveta permite rolar até a última seção em celular deitado. Mensagens de
operações da Oficina aparecem como aviso quando suas ferramentas estão fechadas.

Referências: captura do mantenedor, Painel atual e seus componentes de expansão,
tema do Ultima e atividade dos estilos Sufficit.Blazor.UI. A primeira ação é
abrir Painel. Oficina do mundo fica no topo da gaveta para acesso direto.

1. [concluído] Mover o grupo e ajustar a composição e os avisos.
2. [concluído] Quantificar visibilidade, teclado e rolagem em Chromium, rodar testes e publish Release.
3. [em andamento] Publicar na main e confirmar o resultado no GitHub Pages.
4. [pendente] Converter o plano em atividade após a validação.

A Oficina passa a ser a primeira expansão do Painel. Sai o cartão separado do mapa. O Blazor mantém os estados e mostra notificações externas quando a Oficina está fechada.

Validação local: publish Release, RulesSmoke e git diff --check passaram. Chromium em 1366 × 768, 390 × 844 e 740 × 360 confirmou a Oficina ausente com a gaveta fechada, as sete seções, acesso por teclado, rolagem até o fim, modo Levantar e retorno a off, seletor de arquivo e aviso externo sem sessão de jogo.

Próxima ação: enviar à main e repetir a verificação no Pages.
