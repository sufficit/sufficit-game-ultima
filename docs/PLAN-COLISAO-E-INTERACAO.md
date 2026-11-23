# Colisão e interação com objetos no piloto do Ultima

Aceite: o jogador para ao correr contra troncos, pedras e objetos sólidos,
contorna as bordas, atravessa vegetação baixa e volta a passar após remover um
objeto. Perto de um sólido, E/A identifica o objeto e mantém prioridade para
conversas com NPCs. Validar no cliente worker e main sem mutar o mapa compartilhado.

1. [concluído] Identificar a movimentação, raios dos modelos e ciclo das instâncias.
2. [concluído] Aplicar colisão com deslizamento, acompanhar as instâncias e ligar a inspeção à interface.
3. [concluído] Reproduzir a regressão no navegador, testar colisão, remoção, interação e publish Release.
4. [em andamento] Publicar na main, confirmar no GitHub Pages e converter o plano em atividade.

O cliente aplicava diretamente o deslocamento. Os descritores de pinheiro e
pedra já declaram CollisionRadius. Usar esses raios nos modelos procedurais e
perfis de base nos GLBs sólidos. Colisão deste piloto é local ao movimento do
jogador no navegador. O plano de oclusão de câmera permanece independente.

Na versão ao vivo anterior, o teste correu até z=7,5 m apesar do pinheiro em z=2 m. A implementação calcula contato pelo trajeto, segue a tangente e acompanha a vida da instância. O texto de inspeção usa o mesmo E/A da conversa.

O ensaio encontrou escala 100 vezes menor na pedra GLB de malha única. A transformação de origem é incorporada antes da normalização. Publish Release, RulesSmoke, quatro testes Node e o ensaio Chromium WebGL2 worker e main passaram. O tronco em z=2 m bloqueou em z=1,469 m e o jogador alcançou z=3,34 m no desvio. Toco e pedra bloquearam, remoção liberou, samambaia permitiu passagem, e E examinou o pinheiro sem regressão na Guardiã.

Próxima ação: enviar à main e repetir o ensaio no GitHub Pages.
