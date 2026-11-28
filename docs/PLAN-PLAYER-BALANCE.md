# Player balance

Model segment mass, gravity, support, recovery and falls for the local player.
Provide configurable balance profiles and verify them in both renderers.

1. Complete — inspect support, movement and posture integration points.
2. Complete — implement segment dynamics, balance profiles and reactions.
3. In progress — validate gameplay, publish and verify production.


## Implementation

- Use segment masses normalized to the configured body mass and gravity of
  9.8 m/s². Adult proportions are rounded from [de Leva's published
  anthropometry](https://pubmed.ncbi.nlm.nih.gov/8872282/).
- Estimate the center of mass from the joint pose and compare its projected
  capture point with the planted feet and the reach of a recovery step.
- Run the upright controller and the secondary joint pendulums at 120 Hz.
  Keep the existing leg IK authoritative when a sole is planted.
- Support `normal`, `drunk`, and `focused` through
  `game.balance.setProfile(profile)`. Events can call
  `game.balance.impulse(lateral, forward)` in newton-seconds, in actor axes,
  and inspect `game.balance.state()`. Space recovers once the fall lands.
- This is a reduced-order model for the local dressed player. Ground and
  collision handling still use the current movement envelope and resting pose.

Next action: verify the browser balance and posture scenarios on the public build.

Local Chromium validated the new balance and posture scenarios in both modes.
The existing tall-avatar head-height check was intermittent, as previously
recorded. A matched main-thread probe measured the player animation call at
approximately 0.06 ms mean before and 0.42 ms afterward on this machine.
