# Environments and missions

Open **Painel → Ambientes → Escritório** in Ultima. The office opens in
third person at the reception, with the garden player avatar. Follow the central aisle to the workstations
and enter the glass meeting room through the open doorway.

- On desktop, focus the scene and use WASD to walk.
  Use the arrow keys or drag the mouse to look and hold Shift to move faster.
- On touchscreens, use the left stick to walk and the right stick to look.
- With the scene focused, the controller's left stick moves and its right
  stick turns the camera.

Press **V**, click the mouse wheel, press the controller's right stick
(**R3**), or select **Alternar câmera**. Each input cycles from rear
third person to first person to close third person. The close view follows
behind the character and aims with mouse movement. Scroll to adjust between
1.85 m and 3.80 m in clear space. Walls can reduce the distance.
**Esc** releases the pointer. [Camera controls](USAGE-JOGO.md#camera-views)

Open **Missões** to see the board and filter by station. Use
**Trocar ambiente** to return to the environment picker. The office can
be explored without a connection to the mission API.

Players using the same multiplayer invitation keep the same room when they
change environments. The office shows each player while they are in the
office. A friend who is still in the garden appears after opening the office.

## Author an environment

The built-in [catalog](../src/wwwroot/environments.json) contains the garden and
the office. Each record declares its ID, name, description, and either terrain
settings or an object scene. The latter specifies camera, background, geometry,
and optional stations. A scene may also declare walking bounds, a spawn point,
solid furniture, material finishes, signs, and lights. Add another record to make it selectable in **Ambientes**.
Authored scenes and terrain presets open through the same engine. A scene
with walking bounds is playable. A scene without walking bounds opens as a
station preview. The garden API's shared terrain and NPCs belong to the
default terrain preset. [Engine architecture](../ARCHITECTURE.md#one-engine-and-a-continuous-world)

Players can design environment definitions as JSON. The pilot loads a published
catalog. The in-game editor and publishing permissions for player environments
are future work.

## Player body

The top-level `playerBody` entry in [environments.json](../src/wwwroot/environments.json)
configures the local avatar for both the garden and office. Omitted fields
receive the current avatar's defaults. A saved
[Characters API profile](USAGE-API-CHARACTERS.md) takes precedence when the
server is reachable. This example changes height, mass, build and skin tone.

```json
{
  "playerBody": {
    "species": "homo-sapiens",
    "height": 1.9,
    "mass": 86,
    "build": 1.08,
    "legRatio": 0.46,
    "femurRatio": 0.54,
    "waistCircumference": 0.92,
    "thighScale": 1.2,
    "upperArmScale": 1.15,
    "skin": { "red": 0.47, "green": 0.29, "blue": 0.20 }
  }
}
```

| Field | Valid values | Default |
| --- | --- | --- |
| `species` | `homo-sapiens` | `homo-sapiens` |
| `height` | 1.45–2.10 m | 1.75 m |
| `mass` | 35–220 kg | 70 kg |
| `build` | 0.80–1.20 body-width multiplier | 1.00 |
| `legRatio` | 0.40–0.48 of total height | 0.44 |
| `femurRatio` | 0.42–0.58 of the hip-to-ankle length | 0.50 |
| `armLengthRatio` | 0.30–0.42 of total height, shoulder to wrist | 0.35 |
| `shoulderHeightRatio` | 0.77–0.84 of standing height, measured from the soles to the shoulder pivot | 0.80 |
| `upperArmRatio` | 0.43–0.57 of the shoulder-to-wrist length | 0.17 / 0.35 |
| `waistCircumference` | 0.60–1.65 m around the widest abdominal cross-section | Existing torso contour |
| `chestScale` | 0.80–1.25 chest diameter multiplier | 1.00 |
| `thighScale`, `calfScale`, `upperArmScale`, `forearmScale` | 0.75–1.50 regional diameter multiplier | 1.00 |
| `faceWidth` | 0.85–1.15 head-width multiplier | 1.00 |
| `earScale` | 0–1.40 ear-size multiplier | 0 (legacy silhouette) |
| `skin`, `eyes`, `hair` | RGB object with `red`, `green`, `blue` channels from 0 to 1 | Current avatar palette |

The catalog rejects an invalid profile. The game resolves dimensions at
scene startup. `legRatio` sets the hip height and `femurRatio` changes the
knee position within that length. The remaining segment length belongs to
the tibia. The arm ratios place the elbow and wrist in the same way. The
regional multipliers apply after `build`. An explicit waist circumference
sets the abdominal diameter in meters independently of those multipliers.
The movement solver uses both leg lengths to keep the ankle on its target.
Mass is stored independently of all measurements for future force and inertia
rules. Limb sizes and chest scale describe geometry. Clothing and accessories
use the existing model appearance. [Species and body design](../decisions/humanoid-species-and-body.md)

## Game API

The hosted office uses the
[Ultima API on Sufficit Run](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/health).
For agents, the office board is
`/v1/game/environments/office/missions`. An unauthenticated request returns
`401`. Hosted credentials are stored as separate `author` and `agent` entries
in the workspace Secret `ultima-mission-credentials`. Give each integration
only the role it needs. The browser board can connect with the agent role.

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
