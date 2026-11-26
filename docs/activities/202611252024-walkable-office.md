# Walkable office interior

## Investigation

The office opened as an orbital preview of desks and a floor slab. The
requested play space needed actual-scale rooms, a route between them,
solid furniture, and a camera at human eye height.

## Changes

- Authored reception, open workstations and a glass meeting room in the
  [environment catalog](../../src/wwwroot/environments.json). A central aisle and a
  1.56 m doorway provide circulation. The interior includes a suspended
  ceiling, ceiling fixtures, office furniture, geometric skyline and
  original procedural oak, carpet and stone textures.
- Added a reusable C# walking controller (`SceneWalk`)
  for interior scenes, with collision, sliding, bounded pitch and a
  1.62 m eye-height view. Babylon.js renders the view and captures browser
  input. Static opaque geometry is grouped by material.
- Kept the canvas prominent and placed the mission controls behind the
  **Missões** button using the existing SUI design. Environments without
  walking settings retain the existing orbit behavior.
  [Controls and scene authoring](../USAGE-ENVIRONMENTS.md)

## Validation and delivery

- Release publish and RulesSmoke passed. The Chromium office scenario
  passed locally and on GitHub Pages in both garden renderer modes.
- Chromium traversed reception and the doorway, verified the glass
  blocked walking, looked around by dragging, opened the mission board,
  checked the mobile layout, and returned to the garden. A desktop
  capture was reviewed at eye height.
- The hosted mission API returned 200 to the office's browser after
  opening **Missões**. The browser cleared the key when disconnecting.
- Published revision `56e15c68a5ce4d8ac861417cae896bf9711d60bb`.
  [Walkable office](https://sufficit.github.io/sufficit-game-ultima/environments/?id=office)
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45720613860)
