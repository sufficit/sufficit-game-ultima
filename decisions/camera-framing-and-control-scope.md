# Camera framing for individual and group control

## Current behavior

The player directly controls one character at a time. Garden and Office
use the same three camera modes and share the close camera calculation.

| View | Purpose | Current framing |
| --- | --- | --- |
| Elevated orbit | Explore and see the surroundings | Orbits the current character from above. Scroll adjusts orbit distance in the garden. |
| First person | Act from the character's eye level | Follows the current character's position and look direction. |
| Close third person | Aim and move at close range | Follows behind the current character. Mouse look turns the character, and WASD can move laterally or backward while maintaining aim. |

In close third person, the nominal distance to the camera target is
1.85–3.80 m. The 1.95 m adjustment range is three times the original
0.65 m range. The default remains 2.15 m, and 120 units of wheel input
change the requested distance by 0.08 m. The camera eases to the requested
distance. Walls and terrain can bring it closer. It returns to the
requested distance when the line of sight clears. The adjustment persists
when cycling views within the current scene.

## Planned control of up to five characters

The future group mode gives a single player a selection of up to five
controllable characters and lets them issue orders to several at once.
The camera and the command target set have separate responsibilities.

- **Elevated orbit.** With one selected character, frame that character.
  With two to five, frame the bounds of the selection with screen-space
  padding. Adjust the center and distance as members spread out or regroup.
  Use world bounds and camera collision to constrain the result. The
  elevated view remains the place to read the scene and give group orders.
- **First person and close third person.** Frame the active member for
  individual movement and aiming. Cycling the view preserves the group
  selection and pending orders. Returning to the elevated orbit restores
  the group frame.
- **Simulation.** Use the same input, camera and character systems across
  environments. Put selection, command routing and framing rules in C#.
  Babylon.js applies the camera pose and reports geometry queries. The
  camera reads the selection and never changes it.

Group verification will cover one, two and five selected characters,
wide and narrow layouts, changes to the selection while moving, and
obstructions that force the camera inward. Exercise the worker and
main-thread renderer. Keep the current one-character controls available
throughout the change.

[Engine architecture](../ARCHITECTURE.md#one-engine-and-a-continuous-world) ·
[Player controls](../docs/USAGE-JOGO.md#camera-views)
