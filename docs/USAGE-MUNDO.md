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

## Validação

Com o app em Release, execute `python3 tests/world-store.integration.py`.
O teste sobe servidores em portas efêmeras com diretórios temporários.
O ensaio `tests/world-undo.e2e.cjs` usa dois navegadores e um backend de
teste local para verificar alterações e reversões nas malhas.
