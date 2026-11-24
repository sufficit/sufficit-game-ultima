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
