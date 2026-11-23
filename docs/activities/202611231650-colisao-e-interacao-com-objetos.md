# Colisão do jogador e inspeção de objetos no Ultima

## Investigação

O movimento local somava o deslocamento direto à posição do jogador. Pinheiros
e pedras já declaravam raio de colisão no descritor, mas o movimento não usava
esse dado. Na versão ao vivo anterior, uma corrida frontal alcançava 7,5 m
apesar de um pinheiro plantado a 2 m. A verificação da pedra alternativa também
revelou uma escala de 1,9 cm onde o catálogo indicava 1,9 m.

## Alterações

- O movimento varre o trajeto do jogador contra as bases sólidas e tenta
  deslizar pela tangente após o contato. Usa o raio dos modelos procedurais
  e o perfil da base em GLBs sólidos. Grama, samambaias e outras plantas baixas
  permitem passagem.
- A colisão acompanha o ciclo da instância, a ocultação durante escavação e a
  remoção de objetos plantados. O espalhamento dos sólidos nativos usa sementes
  estáveis e avalia os pontos sobre o terreno base.
- As transformações da origem de GLBs de malha única são incorporadas antes
  da normalização do tamanho. A escala visível acompanha a base de colisão.
- Próximo de um sólido, E no teclado ou A no controle ou no toque permite
  examiná-lo. O diálogo aberto e NPCs próximos mantêm prioridade.
- O escopo físico é o movimento do jogador no navegador. As coordenadas
  autoritativas de agentes e a colisão da câmera seguem em frentes próprias.

## Validação e entrega

- Publish Release, RulesSmoke, quatro testes Node, sintaxe JS e
  `git diff --check` passaram. Permanecem os avisos prévios de nulabilidade.
- O ensaio [world-collision.e2e.cjs](../../tests/world-collision.e2e.cjs)
  reproduziu o defeito antigo e passou em Chromium WebGL2 no worker e main
  do build `20367e4`. No main, o pinheiro a 2 m parou a corrida em 1,469 m.
  No desvio, o jogador passou de 3,34 m em Z. Remoção, samambaia, toco, pedra,
  pinheiro procedural e diálogo com a Guardiã também foram exercitados.
- A inspeção foi testada com E e com o botão A num viewport tátil no worker.
  As operações usaram instâncias locais ao navegador, com a sessão de rede
  desabilitada.
- Publicado na main e conferido no [Ultima](https://sufficit.github.io/sufficit-game-ultima/).
  [Workflow de publicação](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45252769083).
