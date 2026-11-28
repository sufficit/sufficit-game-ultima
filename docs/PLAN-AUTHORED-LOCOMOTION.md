# Authored humanoid locomotion

Use the supplied animation libraries on Ultima's existing dressed humanoid.
The walk must retain foot contacts and animate the upper body in sync with
them. Lying must read as a supported resting pose distinct from crawling.

1. Complete — inspect the supplied packs and reproduce the reported pose.
2. Complete — integrate the selected authored tracks and document provenance.
3. In progress — local validation passed in both renderers. Publish and verify delivery.

Next action: push the validated commit and verify the production build.

## Investigation and implementation

- The prior lying pose rotated the mesh around its standing foot pivot. It
  shared most of its animation with the crawling state.
- The local Quaternius packs supplied neutral `Walk_Loop` and the opening
  supine key of `LayToIdle`. Both were adapted to Innerscene. The game keeps
  its planted foot targets, hand springs, and body proportions.
- The folder also held the Stylized Nature MegaKit and Kenney RPG Audio and
  Impact Sounds archives. Ultima already uses MegaKit assets in world
  decoration. The audio packs contain footsteps and cloth sounds for a
  separate sound pass with surface-specific contacts.
- Standing walk speed is 1.50 m/s for the dressed humanoid. Matching that
  speed to the existing stride slows the walking cadence without changing
  the sprint speed. The lying pose stays centered around the player's
  pelvis and uses skinned joint proxies against the local support plane.
  This approximation is intended for level ground.
- See [asset provenance](../assets/LICENSE.md#authored-humanoid-motion) for
  source file checksums and transform conventions.
