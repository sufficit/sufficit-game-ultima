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
4. [concluído] Notifies de animação. RulesSmoke comprovou loop, pausa,
   transições e fila. A mochila comprovou impacto e som nos dois modos
   no build de produção `f59bfde`.
5. [parcialmente bloqueado] Oficina, undo, migração, catálogo por mundo, filtro
   SHA-256, limpeza de órfãos e gráficos avançados implementados e verificados
   localmente. Os corredores volumétricos passaram em dois navegadores com API
   local, o replay preservou o corredor após reiniciar e o undo convergiu.
   A publicação da API depende do Sufficit Run.
6. [concluído] Validar a entrega estática no GitHub Pages e converter os
   planos comprovados. [Evidências](activities/202611232159-oficina-volumes-e-graficos.md).

Validação por entrega: publish Release, testes relevantes do repositório,
ensaio de navegador no build publicado e conferência do diff antes do commit.

O ensaio local reutilizou a câmera orbital e verificou recuo de 7,50 m para
1,25 m diante de pinheiros, recuperação do zoom, arrasto, roda e descarte do
chunk de origem nos dois renderizadores. A emenda e a mochila passaram.

Pendência. Publicar as alterações da API no Sufficit Run e verificar
undo, corredor e catálogo no ambiente autoritativo. O publicador
`workspace_app_publish` não foi exposto à sessão; o CLI respondeu
que o shell não recebeu a ligação ao publicador. O deployment
existente está saudável, o PVC `world-data` segue vinculado e o mapa
ainda não anuncia `volumeTunnels`. O GitHub Pages serve o cliente validado
no build `4646276`.

Referências da interface. O diálogo existente define a posição e a hierarquia
de fala e ações. O Painel do jogo define o ponto de acesso do Diário e os
componentes Sufficit.Blazor.UI. O guia `anti-ai-slop.md` de Refero orienta o
uso de uma superfície por janela e a retirada de elementos decorativos sem função.
O tema de contraste reutiliza essa superfície e mantém o mesmo layout.
