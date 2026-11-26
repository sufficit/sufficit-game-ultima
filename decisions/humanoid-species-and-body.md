# Humanoid species and individual bodies

## Decision

`Humanoid` is the base anatomical type. It defines a biped body plan, the
shared joint roles for the head, torso, arms and legs, and the measurements
used by the common locomotion and animation systems.

`HomoSapiens` derives from `Humanoid`. It provides human proportions, default
measurements and valid ranges for individual variation. A future elf type
also derives from `Humanoid`. It can define its own proportions and ear
shape while using the same joint roles. The engine identifies a character's
species separately from the definition of its rig.

## Individual body properties

| Property | Role |
| --- | --- |
| Height in meters | Sets body scale. The resolved body supplies eye height, joint positions and collision height. |
| Body mass in kilograms | Represents physical mass for simulation rules that use it. |
| Build and proportions | Define the shape of the body, including shoulder and hip width, limb lengths and body volume. The resolved shape supplies stance, reach and collider width. |
| Skin tone, eye color and natural hair color | Define per-character materials. These properties are independent of body geometry and physics. |
| Facial and ear shape | Define individual variation within species limits. |

The species defines which properties it supports, their defaults and valid
ranges. The character stores its species ID and its chosen values. C#
resolves those values into one body description that supplies the model,
rig, camera and collision dimensions. The current human avatar's 1.75 m
height and appearance are the migration defaults for that avatar.

Clothes, equipment and decorative accessories belong to a later attachment
system. Its inputs will include the resolved body and its attachment points.
The present scope is the species hierarchy and body properties.

[Humanoid customization research](../docs/activities/202611261015-humanoid-parameter-research.md)
