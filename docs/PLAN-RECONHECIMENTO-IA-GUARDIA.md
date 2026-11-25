# Reconhecimento da IA local pela Guardiã

O diálogo deve reconhecer o motor local ativado, inclusive após recarregar.
A conversa local funciona sem sessão e as alterações do jardim continuam a
passar pela API. Falhas de resposta têm mensagem distinta da falta de IA.

1. [concluído] Investigar ativação, inferência e acesso à API.
2. [concluído] Corrigir reconhecimento e conversa local e cobrir a regressão.
3. [em andamento] Validar a compilação, os dois renderizadores e a versão em produção.

Validação local: publish Release, RulesSmoke, janelas em worker e main, teste do jardim em worker com API isolada e inferência real com Gemma 3 270M.

Próxima ação: conferir a revisão no GitHub Pages e exercitar o diálogo publicado.
