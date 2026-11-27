# Humanoid shoulder reference measurements

## Investigation

ANSUR II measures the standing height of the right acromion, a bony
landmark identified externally. The upper-arm pivot is a separate, internal
point. Use the survey for external landmarks and a rigged 3D reference for
joint placement. [ANSUR II handbook, section 6.4.2](https://tools.openlab.psu.edu/publicData/ANSURII-TR11-017.pdf#page=88)

| Model | What it contributes | Asset terms |
| --- | --- | --- |
| [MPFB2 / MakeHuman](https://github.com/makehumancommunity/mpfb2) | Inspectable body mesh, named joint helpers and a game rig. Use as the initial Blender reference. | [Base mesh with CC0 notice](https://github.com/makehumancommunity/mpfb2/blob/d9648f3257a0b9712281d466245f30d656250154/src/mpfb/data/3dobjs/base.obj) |
| [Anny / NAVER Labs](https://europe.naverlabs.com/research/publications/human-mesh-modeling-for-anny-body/) | Parametric bodies for comparing proportions. Its bundled MPFB2 mesh gave the same helper measurements and is one reference lineage. | [Apache-2.0 code and CC0 MPFB2 assets](https://github.com/naver/anny/tree/8f35dc2df1c4062fd2fae86734f9705de50c0c2b#license) |
| [SKEL / Max Planck and Stanford](https://skel.is.tue.mpg.de/) | Independent published biomechanical reference for the shoulder girdle and scapular motion. | [Restricted asset terms](https://skel.is.tue.mpg.de/license.html). Consult the publication for design. |

## Changes

For participants from 1740 through 1760 mm tall, the external landmark
measurements were as follows. Percentiles describe each U.S. Army sample
separately and are reference bands for adult shapes. Characters outside the
bands remain possible. [ANSUR II public CSVs](https://www.openlab.psu.edu/ansur2/)

| Sample | Count | Measurement | P5 (mm) | Median (mm) | P95 (mm) |
| --- | ---: | --- | ---: | ---: | ---: |
| Men | 485 | Acromial height | 1409 | 1434 | 1463 |
| Women | 56 | Acromial height | 1418.5 | 1437 | 1467 |
| Men | 485 | Biacromial breadth | 389 | 415 | 444 |
| Women | 56 | Biacromial breadth | 359 | 383 | 404.2 |
| Men | 485 | Bideltoid breadth | 465 | 510 | 563 |
| Women | 56 | Bideltoid breadth | 421.5 | 471.5 | 505.8 |
| Men | 485 | Axilla height | 1300.2 | 1323 | 1348 |
| Women | 56 | Axilla height | 1315.8 | 1340 | 1367.2 |

Across the full public files, acromial height divided by individual stature
had P5/median/P95 values of 80.40/82.06/83.62% for men (4,082) and
80.51/81.98/83.39% for women (1,986). Compute the ratios per row, then take
percentiles. Separate percentiles describe marginal distributions. When
choosing characters for a fit check, use measurements from the same row to
preserve the relationship among shoulder height, width and axilla height.

In the MPFB2 base OBJ, take the Y bounds from vertices referenced by the
`body` faces and average each joint helper group's distinct vertices.
Normalize the centroid by the sole-to-crown height. The
`joint-r-shoulder` and `joint-l-shoulder` helpers give **80.518%**,
or **1409.1 mm** at 1.75 m. The inner clavicle helpers give **80.191%**,
or **1403.3 mm**. These are rig positions for one base mesh and pose.
The original Ultima reference in commit a0b89cb placed the arm pivot at **1435 mm**. Its
similarity to the survey's external shoulder median cannot establish that
the pivot or the sleeve contour is representative. The API's
`shoulderHeightRatio` interval serves as an authoring limit for a pivot.

## Validation and delivery

Read both public CSVs with Python's CSV reader and applied linearly
interpolated inclusive percentiles to the measured columns. Repeated the OBJ
measurement against the MPFB2 revision linked above and
[Anny's bundled base](https://github.com/naver/anny/blob/8f35dc2df1c4062fd2fae86734f9705de50c0c2b/src/anny/data/mpfb2/3dobjs/base.obj).
The next mesh fit needs a marked acromion, a separately placed arm pivot and
front and side comparisons against rigged references. A central profile and
profiles near the edges of the observed bands should be checked before binding
the skin to the gameplay pose. [Rig implementation plan](../PLAN-HUMANOID-SKINNED-RIG.md)
