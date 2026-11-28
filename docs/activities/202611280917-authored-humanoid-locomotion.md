# Authored humanoid walk and resting pose

## Investigation

The previous lying state shared most joint adjustments with crawling. It
rotated the standing skin around its foot pivot and used a fixed lift.
The walk used a single mirrored oscillator for both arms while the feet
followed a separate distance-driven contact solver.

The supplied Quaternius Standard packs contained a neutral `Walk_Loop` and
a suitable first supine key in `LayToIdle`. The local Stylized Nature MegaKit
already supplies world decoration. Kenney RPG Audio and Impact Sounds contain
licensed footsteps and cloth sounds suitable for a separate sound pass.

## Changes

- Extracted 24 upper-body and pelvis samples from `Walk_Loop` into C#.
  The foot phase drives the curves. Periodic cubic interpolation maintains
  continuous movement at the cycle boundary. The in-game contact solver,
  impact bracing, gestures and the wrist and finger springs still run.
- Adjusted walking speed from 1.65 to 1.50 m/s on the dressed avatar to
  match the planted stride. Sprint speed remains 4.15 m/s.
- Adapted the opening key of `LayToIdle` to the current Innerscene skin.
  `Z` lies on the back around the player's pelvis, while `X` crawls face down.
  The retarget keeps the dressed mesh and its bone lengths. Joint proxies
  resolve resting height on the local support plane.
- Recorded clip names, licences and source file hashes in the
  [asset inventory](../../assets/LICENSE.md#authored-humanoid-motion).

## Validation and delivery

- Terrain WebAssembly build, Release Blazor publish, RulesSmoke, and 16
  JavaScript unit tests passed.
- Chromium passed `skin-feet` and `outfit-posture` on both renderer paths.
  The default and 1.90 m profiles met the existing ankle, joint-length,
  head-bob and arm-swing checks. The posture regression checks that the
  lying pose stays centered, low and stationary.
- Captured a side view of the walk before and after, plus the new resting
  pose. The resting contact approximation was checked on level ground.
- Verified the [GitHub Pages build](https://github.com/sufficit/sufficit-game-ultima/actions/runs/46143601612)
  for commit `a04a321`.
  The public manifest and posture asset matched, and the live posture
  scenario passed with both renderer paths.
