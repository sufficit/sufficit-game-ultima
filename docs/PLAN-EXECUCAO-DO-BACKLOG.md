# Execução dos planos do Ultima

Implementar os planos autorizados no índice e verificar no jogo publicado.
Reaproveitar entregas comprovadas e preservar os critérios pendentes de cada frente.

1. [concluído] Terreno em chunks e câmera orbital. Comprovar streaming,
   descarte, névoa e colisão da câmera em cenário denso. Código e medições
   confirmados no GitHub Pages no build `7aede38`.
2. [concluído] Interface declarativa em Razor. Diálogo e Diário exercitados
   localmente e no build `c630ebb` com ações centralizadas e dois temas.
3. [concluído] Pipeline esquelético e multidões. 100/300 instâncias em dois
   tipos, textura compartilhada e LOD de 3.226 para 876 vértices. A publicação
   e a medição de regime estão no checkpoint 6.
4. [em andamento] Notifies de animação. Comprovar wrap, pausa, transições e dois
   eventos ligados à animação em jogo.
5. [pendente] Revisar os checkpoints restantes de mundo, persistência e assets.
   Resolver as pendências descritas nos planos com a validação correspondente.
6. [pendente] Validar o conjunto no GitHub Pages, atualizar o índice e
   converter os planos que tiverem todos os critérios comprovados.

Validação por entrega: publish Release, testes relevantes do repositório,
ensaio de navegador no build publicado e conferência do diff antes do commit.

O ensaio local reutilizou a câmera orbital e verificou recuo de 7,50 m para
1,25 m diante de pinheiros, recuperação do zoom, arrasto, roda e descarte do
chunk de origem nos dois renderizadores. A emenda e a mochila passaram.

Próxima ação. Usar a timeline para aplicar a coleta no contato do golpe e
enviar os eventos de impacto e som pelo agendador.

Referências da interface. O diálogo existente define a posição e a hierarquia
de fala e ações. O Painel do jogo define o ponto de acesso do Diário e os
componentes Sufficit.Blazor.UI. O guia `anti-ai-slop.md` de Refero orienta o
uso de uma superfície por janela e a retirada de elementos decorativos sem função.
O tema de contraste reutiliza essa superfície e mantém o mesmo layout.
