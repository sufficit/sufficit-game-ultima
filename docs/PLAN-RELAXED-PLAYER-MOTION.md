# Relaxed player motion

Correct the resting silhouette and add measurable elbow lag while walking.
Keep feet planted and preserve the avatar's current proportions and outfit.

1. Complete — measure the present rig and isolate the motion defects.
2. Complete — implement supported rest and independent elbow motion.
3. In progress — Chromium passed locally in both modes. Verify production.

Next action: verify the resting contact patches and full elbow excursion in
the two renderer modes, then verify the GitHub Pages build.

## Measurements

The previous pose aligned its lowest joint proxy to the camera's padded
support height. The spine and fingers retained the lifted prototype shape.
The new pose uses the actual floor plane, calibrated pelvis height, a
supported head and heels, and relaxed palm roll. Chromium samples the
deformed mesh at the skull, back, hips, both hands, and both heels.

The arm change adds a damped phase response at each elbow before driving
the existing wrist and finger lag. The side-view capture measured an elbow
excursion of about 9° before the change and 16° afterward on the default
avatar. Run motion still uses its existing authored/procedural blend.
