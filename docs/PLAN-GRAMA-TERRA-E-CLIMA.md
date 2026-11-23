# Melhorar grama, terra e clima do Ultima

Aceite: grama e trilha com imagens de material legíveis a curta distância, emendas
discretas entre chunks e acabamento coerente com a direção do vale. A luz do céu
e do sol responde ao clima, a vegetação se move em rajadas e a chuva aparece no
mundo 3D. O jogador alterna o clima pelo Painel, sem recarregar o jogo. Worker e
main mantêm o terreno visível e o jogo inicia com fallback em caso de falha de asset.

Referências: vale e paleta atuais, trilha quente como guia de navegação e gramado
mais frio nas margens. Os materiais devem ser quadrados, ortográficos e
repetíveis, com grama curta e terra compactada, adequados à escala do personagem.
Priorizar leitura da trilha, sombras coerentes e chuva fina, sem encobrir os controles.
Clima fica em Painel → Oficina do mundo → Gráficos, junto às preferências existentes.

1. [concluído] Criar as imagens de material e registrar seu uso na cena.
2. [concluído] Integrar os materiais, luz, rajadas e chuva no motor e incluir o controle SUI.
3. [concluído] Validar renderização e interação em worker e main, testes e publish Release.
4. [em andamento] Publicar na main, validar o site e converter o plano em atividade.

ImageGen produziu duas imagens ortográficas quadradas, convertidas em WebP de 256 px para o jogo. A grama e a terra serão projetadas na malha sem retirar a paleta e as edições do mundo.

A composição usa as imagens WebP com máscaras da paleta, luz e sombra de cena numa base ambiente calibrada. O clima é local, persistido e acessível pelo seletor SUI. Chuva em volume próximo e rajadas variam com a transição.

Validação local: Chromium WebGL2 em worker e main, clima por teclado ou clique no seletor SUI, chuva persistente, retorno a céu limpo, rajadas nas lâminas, pintura e fallback sem imagens. Emenda média de 4,85 níveis RGB em 255. Na amostra local, p95 de montagem por chunk foi de 18,2 para 19,1 ms. Publish Release, sintaxe JS, quatro testes Node, RulesSmoke e git diff --check passaram.

Próxima ação: publicar na main e repetir a verificação no Pages.
