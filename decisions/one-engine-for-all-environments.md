# One engine for all environments

## Context

The office started as an object-scene prototype with its own
`SceneWalk` controller and render loop. It reused the garden's character rig
and core locomotion and pose routines. Features such as camera controls
still needed wiring in both places.

The garden and the office now use the [same game loop](../src/wwwroot/js/game-core.js).
The office geometry and its mission board remain driven by the catalog.
Walkable scenes use the common [collision sweep](../engine/Ultima.Terrain/PlayerMovement.cs)
and can supply [solid boxes](../engine/Ultima.Terrain/PlayerMovement.Scene.cs) and traversal bounds.

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
