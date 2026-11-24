# Edições e catálogos do mundo

## Desfazer pinceladas

Na Oficina do mundo, abra **Esculpir o terreno**. Depois de o servidor
confirmar uma edição, **Desfazer edição** remove a última pincelada
confirmada nesta sessão. Em grutas e passagens, a reversão remove
o lote inteiro, incluindo a pintura produzida pelo pincel.
Outras pinceladas na mesma posição continuam no mundo.

O servidor atribui um campo `edit` ao gravar um lote de superfície. O
comando `{"action":"undo_surface","edit":"e…"}` aceita o identificador
de uma edição da sessão autenticada. O journal mantém o lote e a
reversão. O replay materializa somente as edições em vigor. Os
clientes atualizam o mapa quando recebem `world_edited`. Edições
legadas, que não possuem dono de sessão, permanecem no mapa.

## Corredores curvos

Quando o servidor anuncia suporte a volumes, **Corredor curvo** fica disponível
na Oficina. Clique na entrada, no ponto da curva e na saída. O mesher em
C# WebAssembly resolve as faces do volume em uma grade de 0,50 m. A camada
externa do terreno e o piso subterrâneo são selecionados pela altura do
personagem. O corredor inteiro é desfeito em uma única ação.

A API `tunnel` aceita de três a oito pontos e 60 m de comprimento total.
O raio fica entre 1,8 e 3 m. Cada ponto indica a altura da superfície
escolhida. O servidor deriva os pontos internos abaixo dela e registra
toda a trajetória como um lote no journal.

## Catálogo de cada instalação

| Variável | Efeito |
| --- | --- |
| `ULTIMA_DATA_DIR` | Raiz gravável do mapa e dos assets. |
| `ULTIMA_WORLD_ID` | Identificador opcional de instalação. Mapa e assets são gravados sob `worlds/<id>/` na raiz. Sem valor, continua usando o caminho histórico. |
| `ULTIMA_UPLOAD_SHA256_ALLOWLIST` | Lista de hashes SHA-256 de GLBs aceitos por upload. Separe por vírgula, espaço ou linha. Ausente mantém o fluxo de upload anterior. |
| `ULTIMA_ASSET_GC_DAYS` | Liga a coleta dos GLBs órfãos com a idade mínima configurada, a partir de sete dias. |

Uma instalação serve um mundo. Para hospedar outro, use a instância
que aponta para o respectivo diretório. A lista de uploads por SHA-256
vale para os arquivos enviados por jogadores. Os modelos gerados
continuam no fluxo de geração do servidor.

A coleta roda na subida do servidor e a cada 24 horas quando configurada.
Ela verifica o catálogo gravado e só remove arquivos de nomes gerados pelo
servidor, fora do catálogo e após o prazo de retenção. Um catálogo ausente,
parcial ou ilegível interrompe a limpeza.

## Relógio, céu e clima

Abra **Painel do jogo → Tempo e clima**. O primeiro relógio mostra
GMT, referência do mundo. O segundo mostra a mesma data e hora no
fuso local. O jogo usa o fuso do navegador no modo automático.
Para escolher outro fuso, busque pelo nome da localidade
(por exemplo, `London` ou `Sao Paulo`). Limpar o campo restaura
o modo automático. A seleção fica salva no navegador e acompanha
as regras de horário de verão do fuso escolhido.

O ciclo tem duração real e começa com a data e a hora UTC do
equipamento. Ao abrir o jogo e a cada hora, o cliente consulta
`/v1/worlds/ultima/time` no servidor do Ultima. Se a API ficar
indisponível, o relógio segue contando a partir da última sincronização.
Na primeira visita sem acesso à API, vale o UTC do equipamento.
O céu é calculado para Greenwich. A latitude fixa determina o
trajeto do Sol ao longo do ano e a duração do dia. A fase da Lua
é aproximada e exibida com a fração iluminada. O fuso local muda
apenas a leitura do horário.

O painel permite escolher céu limpo, nublado, neblina, chuva e
chuva forte. Durante esta etapa, o clima selecionado vale para a
cena do jogador e fica salvo no mesmo navegador.

## Validação

Com o app em Release, execute `python3 tests/world-store.integration.py`.
O teste sobe servidores em portas efêmeras com diretórios temporários.
O cenário `world-undo` da suíte C# usa dois clientes em worker para
verificar alterações e reversões nas malhas. Para rodá-lo, suba o
servidor Release com um diretório de dados descartável e use seu endereço
HTTP em `ULTIMA_API`.

```bash
ULTIMA_SITE=https://sufficit.github.io/sufficit-game-ultima/ \
ULTIMA_API=http://127.0.0.1:8090 \
dotnet run --project tests/WorldCollisionE2E -c Release -- world-undo
```
