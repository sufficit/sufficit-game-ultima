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
também o alcance lateral. A passada do jogador cobre 1,16 m por ciclo.
Durante o balanço, o pé avança do último apoio para a próxima aterrissagem,
com aceleração na saída e desaceleração antes do contato. O tornozelo
inclina a sola e os braços alternam com a perna oposta. Giros no lugar
podem iniciar uma passada de
reposicionamento. Teleportes liberam os contatos antigos. O terreno e
os obstáculos continuam determinando a altura sob a sola.

O Andarilho e os avatares remotos continuam com o solucionador sagital.
O solucionador local usa a altura de apoio e uma sola nivelada durante
o contato. A inclinação pela normal do relevo depende de uma futura
sondagem da superfície em mais de um ponto.
