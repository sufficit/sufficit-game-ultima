# One engine for all environments

## Context

The garden uses the main game loop. The office started as an object-scene
prototype and still starts a separate scene with its own [C# controller](../src/Environments/SceneWalk.cs).
It already uses the same character rig and core locomotion and pose routines.
The separate entry points explain why features added in one place can require
extra integration in the other.
[Garden scene](../src/wwwroot/js/game-core.js) ·
[Office scene](../src/wwwroot/js/environment-scene.js)

## Decision

Use one game loop and one C# simulation for all playable environments.
The [content catalog](../src/wwwroot/environments.json) describes what a place
contains. Terrain and building geometry both contribute collision and support
surfaces to the same physics system. Model instances, lights, spawn points and
interaction targets enter the world through the same content-loading path.
An environment ID identifies content and can scope missions.

First, load the garden and the office as separate selections through the
same engine. Verify that the same player, controls and camera modes work in
both, including collision against terrain and building geometry. Exercise
the OffscreenCanvas worker and the main-thread fallback. Remove the separate
office controller once the common path passes those checks.

Then place the office at world coordinates. Walking into it uses the same
player, camera and session. The engine can stream nearby geometry as the player
travels. The office location and the map connection are future work.
