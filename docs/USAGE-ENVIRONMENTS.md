# Environments and missions

Open **Painel → Ambientes → Escritório** in Ultima. The office has a separate 3D scene
with an orbit camera and stations for **Operações**, **Pesquisa**, and
**Revisão**. Station buttons filter the mission board. The scene and switching
back to the garden are available independently of the API.

## Author an environment

The built-in [catalog](../src/wwwroot/environments.json) contains the garden and
the office. Each record declares its ID, name, description, and either terrain
settings or an object scene. The latter specifies camera, background, geometry,
and optional stations. Add another record to make it selectable in **Ambientes**.
The game uses the same scene renderer for every object scene. Additional terrain
presets open locally. The garden API's existing shared terrain and NPC rules remain
attached to the default terrain preset.

Players can design environment definitions as JSON. The pilot loads a published
catalog. The in-game editor and publishing permissions for player environments
are future work.

## Game API

Configure `ULTIMA_MISSIONS_AUTHOR_KEY` and `ULTIMA_MISSIONS_AGENT_KEY` on the
ASP.NET Core server with distinct random tokens of at least 32 characters.
Both roles can read missions and submit events. The author role can also
publish definitions. The office client accepts a key when connecting and
keeps it in browser memory until disconnection. Use HTTPS for remote servers.

| Request | Role | Purpose |
| --- | --- | --- |
| `GET /v1/game/environments/{environment}/missions` | Author or agent | List missions, objectives, counts, and the last recorded event |
| `POST /v1/game/environments/{environment}/missions` | Author | Publish a player-designed mission definition |
| `POST /v1/game/environments/{environment}/missions/{id}/events` | Author or agent | Record work against a mission objective |

Use `Authorization: Bearer <key>` with each request and
`Content-Type: application/json` when posting. Here is a mission definition.

```json
{
  "id": "daily-review",
  "title": "Review the daily report",
  "station": "review",
  "objectives": [
    { "kind": "task.started", "target": "daily-report", "required": 1 },
    { "kind": "task.reviewed", "target": "daily-report", "required": 1 }
  ]
}
```

An agent reads the board, performs work in its own runtime, and reports the
relevant event. The supported event kinds are `task.started`,
`task.completed`, and `task.reviewed`. They can be combined in a definition
and use a target chosen by the mission author.

```json
{
  "id": "review-job-42",
  "kind": "task.reviewed",
  "target": "daily-report",
  "agent": "Reporting agent"
}
```

A matching event increments one objective. A mission completes once every
objective reaches its count. Each event ID identifies one submission in its
mission. Retrying the same payload returns current progress without counting
it again. Reusing that ID with different data returns `409`. An event that
matches no objective returns `422`. The agent name is a display label
provided by the caller and the key authorizes the request.

The API keeps an independent board per environment ID. Its keys are shared across
those boards in this pilot. It supports
authoring through the API. The browser board is for watching progress.
Agents are launched and managed by the operator's own service. The mission
store uses `missions.json` in `ULTIMA_DATA_DIR` and expects one API
writer process per data directory. The mission API requires server keys
even when the client was deployed separately through GitHub Pages.
