# Humanoid rig diagram

## Investigation

The animation guide described the humanoid hierarchy and coordinate system.
The rig needed a visual reference that matched the default player profile.

## Changes

Added a [joint map](../figures/humanoid-rig.svg) to the
[animation guide](../USAGE-ANIMACOES.md). The front and side projections
use the same scale. Enlarged right-hand and right-foot views show the
finger and toe chains. The legend identifies spherical pivots, hinge
pivots, the actor root, and parent–child links.

## Validation and delivery

Generated the points directly from the C# default 1.75 m humanoid profile.
Verified all 73 joint links in each full projection and both detail chains.
Rendered and inspected the SVG at 1460 × 1050 pixels.
