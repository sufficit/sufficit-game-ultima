# Independent humanoid proportions

## Investigation

The first human profile set hip height independently of total height but split
the hip-to-ankle span evenly. The rig and inverse kinematics both assumed
equal leg segments. Build also scaled the chest and limbs together, which
could not describe individuals of the same mass with different silhouettes.

## Changes

- Resolved femur and tibia lengths and shoulder-to-wrist proportions in the
  [C# body profile](../../models/Ultima.Models.Core/HumanoidBody.cs). The
  default ratios preserve the original joint measurements.
- Added an explicit waist circumference, a chest multiplier, and separate
  diameter multipliers for the thighs, calves, upper arms, and forearms.
  The shape is symmetric. The abdomen uses a circular cross-section and
  mass is stored independently of the geometric parameters.
- Applied the dimensions to the local avatar's rig, rendered body and
  movement clearance. The C# solver uses both leg segment lengths to reach
  the ankle target. The renderer twists the knee about the hip-to-ankle
  vector, preserving the ankle position.
- Documented the [catalog fields and bounds](../USAGE-ENVIRONMENTS.md#player-body).

## Validation and delivery

- Built model packages, assets, terrain WASM, the Blazor Release publish and
  the C# browser test project. RulesSmoke verified two bodies at 1.80 m and
  90 kg with distinct segment lengths, circumference, and limb diameters.
  It also checked three-dimensional ankle placement for both bodies.
- Chromium with SwiftShader passed the customized body scenario in the garden
  and office in worker and main-thread modes. The obstacle step and office
  traversal regressions also passed on the local Release build.
- Verified both render modes on public build
  `07c7c2f6f1b10e7256183e0753c9762b4cd897bd`.
  [GitHub Pages run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45835006491)
