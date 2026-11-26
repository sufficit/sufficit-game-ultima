# Humanoid customization research

## Investigation

| Project | What to study |
| --- | --- |
| FreeSO and Simitone | FreeSO uses C# and MonoGame. It simulates the world and synchronizes avatar appearance as metadata. Both projects use original game data. Study the data flow for persistent appearances and groups. [FreeSO](https://freeso.org/about/) · [Simitone](https://github.com/riperiperi/Simitone) |
| OpenTS2 | A Unity reimplementation of The Sims 2 with public source. It is a second reference for how a life simulator loads game data. [Source and setup](https://github.com/LazyDuchess/OpenTS2) |
| SimSim | A research prototype that models household objects and character needs. It provides an example of autonomous behavior for groups of characters. [Paper](https://arxiv.org/abs/2008.11258) · [Source](https://github.com/lsoros/simsim) |
| MakeHuman and MPFB | Body sliders cover height, build, muscle and proportions. Morph targets blend vertex offsets. MPFB can refit clothing and other child assets when the body changes. Its material system handles skin separately. [Body controls](https://ww.makehumancommunity.org/makehuman/docs/modeling_the_body.html) · [MPFB model editor](https://github.com/makehumancommunity/mpfb2/blob/master/docs/ui/model/model.md) · [MPFB materials](https://www.makehumancommunity.org/mpfb/docs/materials/index.html) |

MakeHuman and MPFB publish their core assets under CC0 and their application
code under AGPL and GPL, respectively. MPFB is a candidate for offline asset
authoring. This research used original Ultima models and imported no assets
or source code. [Project licensing](https://www.makehumancommunity.org/about/license.html)

## Changes

Documented a proposal for Ultima. The existing
[model builder](../../models/Ultima.Models.Core/ModelContracts.cs) accepts seed
and detail. The
[editable placement contract](../../models/Ultima.Models.Core/PlacementModels.cs)
already shows how to declare editable keys, defaults, ranges, and patch
validation. Its current values are numeric. Humanoid appearance needs
typed color values and, later, asset selections.

The [current villager](../../models/Ultima.Models.Characters/Villager.cs)
builds with fixed height, proportions and colors. The
[anatomy generator](../../engine/Ultima.Terrain/PlayerAnatomy.cs) already
accepts a height. The
[game loop](../../src/wwwroot/js/game-core.js) still uses fixed values
when it initializes the local player and sets first-person eye height.

| Proposed parameter | Consumers |
| --- | --- |
| Height in meters | Body geometry, joint pivots, eye height, collision height and step reach |
| Body build | Body widths, limb girth, stance width, collider radius and clothing fit |
| Mass in kilograms | A separate value for future force and inertia rules |
| Skin, hair, eye and clothing colors | Per-instance materials. Skin tone is independent of body shape and simulation rules. |
| Proportions, facial details, hairstyles and outfits | Later extensions using original parts or reviewed assets and matching attachment points |

Start with an editable player profile for height, body build and skin, hair
and clothing colors. Keep the current appearance as the default for
existing characters. One validated C# profile should produce the visual
descriptor and the simulation measurements. Keep material changes local
to the avatar instance. Share meshes for identical bodies and rebuild
geometry only when a shape parameter changes.

The profile belongs to each character. It stays attached when the player
changes environments or later changes the active member of a group. Keep
appearance data separate from schedules, needs, skills and inventory.

## Validation and delivery

Reviewed primary project documentation and the Ultima source. This activity
delivers research and a technical proposal. Implementation should verify
both body extremes in the garden and office, including foot contact,
doorways, eye height, and the appearance of five distinct characters in
the same scene.
