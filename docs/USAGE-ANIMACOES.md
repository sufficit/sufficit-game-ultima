# Animação de personagens nas DLLs

`scripts/build-models.sh` inclui as definições compiladas no pacote de
personagens. Um modelo pode declarar nomes de clipe com `ModelBuilder.Action`.
Cada clipe contém duração em segundos, parâmetros da pose e marcas
`name`, `time`, `payload`. O tempo da marca é normalizado no intervalo
(0, 1]. O builder ordena as marcas antes da serialização.

O motor usa o mesmo relógio para pose e marcas. O runtime em C# verifica
o intervalo entre os frames, incluindo a virada de ciclo em clipes com loop.
Uma pausa mantém o relógio. Trocar a ação descarta as marcas que ainda
não foram cruzadas. O evento passa pela fila de gameplay do agendador.

A escavação emite `hit/collect` na metade do gesto e `sound/dig` em 62%.
O primeiro confirma a coleta. O segundo toca o impacto na camada
de áudio depois que um gesto real do jogador libera a reprodução.

Para a multidão, `BakeCrowdWalk` anexa as matrizes esqueléticas do ciclo
aos modelos. Uma linha da textura armazena todos os ossos de um quadro,
mais uma matriz identidade. O Babylon reproduz a textura na GPU com
fases independentes por instância. Os dois LODs de cada tipo usam
a mesma paleta e o mesmo esqueleto.

## Anatomia e apoio dos pés

O WASM fornece a pose neutra em metros com `GetAnatomy(espécie, altura)`.
Cada articulação contém `name`, `parent`, `x`, `y`, `z` e `motion`.
As coordenadas seguem o espaço do modelo, com +Y para cima e +Z para a
frente. O pai vem antes do filho, e `root` é o nó do personagem.
`createJointHierarchy` transforma a lista em pivôs para as malhas.
`motion` descreve dobradiças e juntas esféricas para orientar a criação
dos controladores. A hierarquia deste estágio é cinemática.
[Definições das espécies](../engine/Ultima.Terrain/PlayerAnatomy.cs)

| Espécie | Referência da altura | Estrutura |
| --- | --- | --- |
| `humano` | Estatura | Pelve, coluna, cabeça, mandíbula, braços, pernas e dedos |
| `canino` | Cernelha | Membros dianteiros e traseiros, patas e cauda |
| `equino` | Cernelha | Membros dianteiros e traseiros, cascos e cauda |
| `ave` | Altura da ave | Pernas, bico, cauda e asas articuladas |

O jogador usa a hierarquia humana no rig. Dedos, mandíbula e outras juntas
secundárias estão disponíveis como pivôs. Para deformá-los em cena, as
malhas precisam de grupos separados ou pesos de skinning. Os perfis
animais fornecem a pose neutra para construir malhas e controladores
próprios de locomoção.

A fase da passada acompanha a distância percorrida. No jogador, o C#
mantém a posição horizontal e o rumo de cada sola em coordenadas do
mundo durante o apoio, inclusive quando o corpo gira. O quadril resolve
também o alcance lateral. Nas curvas e inversões, o plano de flexão do
joelho acompanha parcialmente o rumo da sola. A perna gira em torno do
eixo quadril–tornozelo, preservando a posição do tornozelo e a orientação
da sola em apoio. A passada do jogador cobre 1,16 m por ciclo.
Durante o balanço, o pé avança do último apoio para a próxima aterrissagem,
com aceleração na saída e desaceleração antes do contato. O tornozelo
inclina a sola e os braços alternam com a perna oposta. Giros no lugar
podem iniciar uma passada de
reposicionamento. Teleportes liberam os contatos antigos. O terreno e
os obstáculos continuam determinando a altura sob a sola. Ao parar, o
jogador traz um pé de cada vez para debaixo do quadril. Cada sola segue
a altura do próprio apoio, e a oscilação do tronco ligada à marcha
desaparece na pose neutra.

Em mudanças de rumo, o jogador progride conforme o corpo se alinha
ao caminho permitido pela colisão. O controle usa a orientação visível
e volta à velocidade de regime quando alinha. A passada local também
usa uma oscilação lateral menor do tronco. Nas inversões fechadas, o
quadril gira sobre o próprio eixo. O tronco acompanha a rotação com uma
pequena defasagem, enquanto os pés alternam o apoio perto do corpo.
A primeira passada começa enquanto o quadril termina de se alinhar.
O pé que pousa no fim do giro dá continuidade à marcha.

O Andarilho e os avatares remotos continuam com o solucionador sagital.
O solucionador local usa a altura de apoio e uma sola nivelada durante
o contato. A inclinação pela normal do relevo depende de uma futura
sondagem da superfície em mais de um ponto.

## Contato entre personagens

O motor usa caixas orientadas medidas nas malhas próximas na pose de
cada quadro. O C# varre as caixas durante o deslocamento, limita a
penetração e conserva a componente tangencial do movimento. Poses
recebidas em um local ocupado são projetadas para um ponto livre.
Fora da região próxima ao jogador ou enquanto faltarem as malhas, a
simulação usa uma caixa corporal compacta.

Um choque que corta a passada aciona uma recuperação cinemática com
recuo do tronco e menor impulso. Na pose local, o solucionador verifica
a distância lateral entre os tornozelos e a largura projetada das solas.
Quando as pernas iriam se cruzar, desloca prioritariamente o pé no ar e
levanta o pé corretivo quando necessário. Os braços usam pivôs levemente
abertos na pose neutra. As caixas são aproximações da geometria da pose.

### Regra de física dos corpos

Dois corpos não devem ocupar o mesmo lugar no espaço. A regra vale
durante o deslocamento e durante a animação, incluindo gestos e mudanças
de pose de personagens parados. Depois de atualizar as poses próximas ao
jogador, o C# verifica as caixas orientadas novamente e separa os
personagens antes de desenhar o quadro. A prioridade preserva a Guardiã
fixa e o apoio do jogador quando há um avatar remoto que pode ser
reposicionado.

### Resposta nas articulações

Cada braço humanoide tem malhas de braço e antebraço ligadas pelo
cotovelo. A mão segue o punho. Em caso de contato, o motor tenta
flexionar o cotovelo e recolher o ombro antes de mover os personagens.
Quando dois braços bloqueiam um ao outro, testa a retração simultânea.
O C# só aceita a pose que libera as caixas em contato.

No jogador, o pé no ar pode buscar um ponto mais próximo sem mover a
raiz nem trocar a pose da outra perna. O solucionador recalcula o
quadril, joelho e tornozelo com a altura do apoio naquele ponto.
Um corte superior a 12 cm sob impulso suficiente aciona o tropeço.
A separação corporal atende os contatos que resistem aos ajustes.


## Aimed locomotion and support

The close third-person view can aim the character independently of travel.
The foot contact solver uses actual movement in the character's local frame
to place the next step. Sidesteps and backward steps use a shorter stride.
A lateral step leads with the foot on that side and keeps the other foot in its lane.

When the character stops, the solver returns the feet under the hips one at
a time. A turn with no translation uses compact steps around the body.
Terrain and obstacle height queries still determine the contact height.
