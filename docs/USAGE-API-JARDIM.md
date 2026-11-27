# API do jardim e da Guardiã

A mesma sessão usada para jogar por HTTP autoriza edições. Crie uma sessão
com `POST /v1/game/sessions` e passe o token em `X-Game-Token`. Descubra
o jardim pelo `GET /v1/game/garden`. O mapa compartilhado está em
`GET /v1/game/map` e os avisos de alteração em `GET /v1/game/events`.

| Família | Rota de escrita | Ações |
| --- | --- | --- |
| Terreno | `POST /v1/game/garden/terrain` | `paint`, `sculpt`, `path` |
| Plantas | `POST /v1/game/garden/plants` | `place`, `remove`, `customize` |
| Decoração | `POST /v1/game/garden/decor` | `place`, `remove`, `customize` |
| Composição | `POST /v1/game/garden/edits` | Lote que combina as famílias |
| Guardiã | `POST /v1/game/garden/dialog` | Diálogo com IA e aplicação no mesmo turno |

O jardim tem raio de 40 m a partir de `(0,-14)`. `x,z` são coordenadas
absolutas em metros. Os pincéis aceitam `r` de 0,5 a 8 m e o relevo
aceita `dh` de -2 a 2 m por ação. `path` recebe de 2 a 16 pontos e
interpola pincéis. As duas famílias de objetos aceitam `rot`, em radianos,
e `scale` de 0,05 a 8. Um lote aceita até 24 ações e 96 operações após
a expansão. Para substituir objetos já colocados, consulte seus UIDs no mapa.

Exemplo de um caminho com iluminação e canteiro, enviado em
`POST /v1/game/garden/edits`.

```json
{
  "actions": [
    {"action":"path","mat":"dirt","r":1.4,"points":[{"x":0,"z":-13},{"x":1,"z":-8},{"x":0,"z":-3}]},
    {"action":"place","asset":"prop.lantern","x":2.5,"z":-10},
    {"action":"place","asset":"flora.flowers","x":-2.5,"z":-10}
  ]
}
```

A edição é validada e gravada em um único registro. A resposta contém
`applied`, `revision`, operações normalizadas e o mapa confirmado. O
cliente pode atualizar uma cena a partir desse mapa. Outros clientes
recebem `world_edited`. Um erro de validação deixa o lote sem efeito.

Para falar pela API, fique a até 4,5 m da Guardiã e envie
`{"message":"Abra uma trilha de terra e ilumine a entrada."}` a
`/v1/game/garden/dialog`. A Guardiã pode responder com `applied: 0`
numa conversa ou quando precisar de detalhes. `503 ai_unavailable`
indica ausência de provedor. A API de diálogo usa o modo de IA do servidor
(`NPC_SERVER_AI`, `NPC_LLM_URL`, `NPC_LLM_KEY`, `NPC_LLM_MODEL`).
O cliente pode usar a IA local quando o modelo na GPU ou CPU está pronto.
O plano local usa a mesma rota de edição e as mesmas validações.

## Modelos que aceitam personalização

A seção `models["*"]` do catálogo descreve o contrato comum para qualquer
objeto colocável. Isso inclui modelos importados pela Oficina. Use
`parameters` em `place` para criar uma variante do mesmo asset ou em
`customize` para alterar a instância pelo UID. Campos omitidos conservam
seu valor. O UID e a posição são preservados.

- `scaleX`, `scaleY`, `scaleZ` ajustam largura, altura e profundidade
  separadamente com multiplicadores entre 0,05 e 8. Cada eixo sem override
  herda a escala uniforme original, `scale`.
- `rotationY` ajusta a rotação horizontal em radianos, de -2π a 2π. Sem
  override, herda `rot`.
- Um modelo que implementa `IEditablePlacementModel` pode declarar campos
  adicionais com unidade, valor padrão e limites, e controlar a validação
  e aplicação do ajuste. Registre a implementação em `PlacementModels`
  para expô-la no catálogo e no contexto da Guardiã. Um parâmetro que
  modifica uma parte específica precisa de suporte no modelo renderizado.
- A luminária declara `poleHeight` de 0,5 a 5 m antes da escala e
  `lightIntensity` de 0 a 3, com padrões de 1,4 m e 1,05.

```json
{"actions":[{"action":"customize","uid":"uid-da-samambaia","parameters":{"scaleY":2,"rotationY":1.2}}]}
```

The Guardiã adjusts up to two nearby placed lanterns in response to supported
relative commands. “Aumente a altura dessas luminárias” adds 0.6 m to the pole.
“Aumente a intensidade da luz dessas luminárias” increases intensity by about
25%. “Quero luminárias mais altas e um pouco mais fortes” adjusts both.
Each command preserves the placement and any parameter it does not target.
The server enforces the model limits. Free-form requests use the same editing
contract through the garden planner.

### Local CPU tool pilot

Select **Qwen2.5 0.5B · ferramentas** in the game panel to try native tool calls.
The Guardian shows the proposed change in metres. **Aplicar alteração** sends it
to the game API. Dismissing the proposal leaves the map unchanged. This pilot
exposes one garden tool. The server validates the session, proximity to the
Guardian, and the placement limits before applying the full edit batch.

`POST /v1/game/garden/tools` takes the player's `X-Game-Token` and this body:

```json
{"name":"adjust_lantern_height","arguments":{"delta_m":1}}
```

Use a signed metre displacement with centimetre precision. An out-of-range
target rejects the batch with `lantern_height_out_of_range`. Use the returned
`applied` count and `map` to update the scene. This is a relative mutation, so
read the map before resubmitting if the result was lost.

A família de jogo continua em `/v1/game/command` para movimento, interação,
construção e escavação avançada, e a família de modelos em `/v1/assets`.
A Guardiã usa as ações registradas no catálogo do jardim. A remoção
seleciona colocações do mapa pelo UID.

### Memória da Guardiã no navegador

No diálogo, `/lembrar minha flor favorita é a orquídea` grava a nota e
`/memórias flor` consulta até cinco notas. Sem termos, a consulta lista
as mais recentes. A inferência local recebe até três notas por coincidência
de palavras, ignorando acentos e maiúsculas. Cada nota comporta 300 caracteres.
O escopo combina servidor, sala, jogador e Guardiã. Os dados ficam no
`localStorage` da origem do jogo e o histórico da conversa fica no IndexedDB.

Um módulo do host pode trocar o provedor após a inicialização. Importe pela
mesma URL usada pelo jogo para compartilhar a instância do módulo.

```js
const { useMemoryPlugin } = await import(new URL('js/guardian-genius.mjs', document.baseURI));
useMemoryPlugin(meuProvedor);
// Para restaurar o provedor local do jogo:
useMemoryPlugin();
```

O provedor implementa `save(scope, text)`, que resolve com `{ id }` após
gravar, e `search(scope, query, limit)`, que resolve com uma lista ordenada
de objetos com `text`. Ele é responsável pelo isolamento do escopo recebido.
Uma troca afeta as operações seguintes e não migra notas entre provedores.
A fábrica independente `createLocalStorageMemory(getStorage, namespace)` em
`js/local-storage-memory.mjs` permite reutilizar o armazenamento em outro host.
