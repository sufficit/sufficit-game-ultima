# API do jardim e da Guardiã

A mesma sessão usada para jogar por HTTP autoriza edições. Crie uma sessão
com `POST /v1/game/sessions` e passe o token em `X-Game-Token`. Descubra
o jardim pelo `GET /v1/game/garden`. O mapa compartilhado está em
`GET /v1/game/map` e os avisos de alteração em `GET /v1/game/events`.

| Família | Rota de escrita | Ações |
| --- | --- | --- |
| Terreno | `POST /v1/game/garden/terrain` | `paint`, `sculpt`, `path` |
| Plantas | `POST /v1/game/garden/plants` | `place`, `remove` |
| Decoração | `POST /v1/game/garden/decor` | `place`, `remove` |
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

A família de jogo continua em `/v1/game/command` para movimento, interação,
construção e escavação avançada, e a família de modelos em `/v1/assets`.
A Guardiã usa as ações registradas no catálogo do jardim. A remoção
seleciona colocações do mapa pelo UID.
