# Segment mass and player balance

## Investigation

The player previously used authored poses and foot IK, with a temporary
brace when colliding or tripping. No segment mass or support calculation
decided whether the player should recover or fall.

## Changes

The C# model uses 9.8 m/s² and distributes the configured body mass using
rounded proportions informed by [de Leva (1996)](https://pubmed.ncbi.nlm.nih.gov/8872282/).
For a 70 kg player, the total weight is 686 N.

| Segment | Fraction of body mass | Scope |
| --- | ---: | --- |
| Head | 7.0% | Head and neck contribution |
| Thorax | 16.0% | Upper trunk |
| Abdomen | 16.0% | Lower trunk |
| Pelvis | 11.0% | Hip assembly |
| Upper arm | 2.7% | Each side |
| Forearm | 1.6% | Each side |
| Hand | 0.7% | Each side, fingers included |
| Thigh | 14.2% | Each side |
| Shin | 4.4% | Each side |
| Foot | 1.4% | Each side, toes included |

The weighted center comes from the current articulated pose. An inverted
pendulum estimates where the body can regain support. Persistent
overreach triggers a directional fall. Joint pendulums drive secondary
motion under gravity, while planted feet use the existing IK solver.
The local player uses the calibrated resting pose once the fall completes.
Jump and balance share gravity. Space recovers when the player is down
and restores movement as the avatar stands.

| Profile | Feedback stiffness (s⁻²) | Damping (s⁻¹) | Recovery reach (m) | Grace (s) |
| --- | ---: | ---: | ---: | ---: |
| `normal` | 26 | 6.2 | 0.22 | 0.20 |
| `drunk` | 17 | 2.7 | 0.10 | 0.15 |
| `focused` | 35 | 9.0 | 0.29 | 0.25 |

Game events can await `game.balance.setProfile(profile)`,
`game.balance.impulse(lateral, forward)` and `game.balance.state()`.
Impulse is in N·s, in local actor axes, capped at 350 N·s.
The profile survives player recovery. This is a reduced-order model for
the local dressed player. Contact during the fall uses the existing
collision envelope and local support plane.

## Validation and delivery

- Terrain WebAssembly, Release Blazor publish, standalone API build,
  RulesSmoke and 16 JavaScript tests passed. Simulation tests checked
  segment totals, posture-dependent center, profile sway, supported and
  airborne cases, mass-dependent impulse response, and agreement at
  30 and 144 frames per second.
- Chromium verified event profile changes, directional falling and Space
  recovery in both modes. The posture scenario also passed in both modes.
  The existing tall-avatar head-height assertion was intermittent
  locally, as recorded in [the prior activity](202611280942-relaxed-player-motion.md).
- An equivalent main-thread walk probe measured a mean player animation
  call of approximately 0.06 ms before and 0.42 ms afterward on the test
  machine. This measurement covers the animation call at coarse browser
  timer resolution.
- A recorded 110 N·s lateral impulse on the 70 kg avatar settled in the
  focused profile and produced a fall in the drunk profile.
- Verified commit `29b5e56` on the
  [GitHub Pages deployment](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46146744382).
  Both the balance and posture scenarios exited successfully on that
  public build in both renderer modes.
