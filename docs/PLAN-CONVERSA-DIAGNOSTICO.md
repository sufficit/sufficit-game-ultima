# Histórico da conversa e diagnóstico

Mostrar perguntas e respostas na janela do NPC e aceitar relatos de erro
pela API com um identificador para investigação.

1. [concluído] Adicionar histórico rolável usando a janela e os tokens do
   Ultima. Reidratar as conversas do Diário sem duplicar os turnos da sessão.
2. [concluído] Enviar relato explícito com descrição opcional e estados de
   versão, renderização e IA. Validar e gravar na API com limites de tamanho
   e frequência. Preservar o conteúdo de conversas locais no navegador.
3. [em andamento] Validar API isolada, Chromium e Release. Publicar e conferir as
   duas implantações. Registrar evidências no documento de atividade.

Referências visuais: captura fornecida, DESIGN.md e a conversa já renderizada.
O histórico ocupa uma área própria acima do compositor. A fala mais recente
fica visível na abertura e o leitor pode rolar para trás. O relato reaproveita
inputs, botões, e estados da Sufficit.Blazor.UI e só abre a pedido do jogador.

Próxima ação: exercitar a conversa e a API com dados isolados.
