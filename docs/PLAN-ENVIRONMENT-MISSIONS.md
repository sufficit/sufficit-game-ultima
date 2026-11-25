# Data-driven environments and missions

1. [complete] Replace the office-specific scene and mission routes with a
   content catalog, reusable renderer profiles, and environment-scoped missions.
2. [complete] Verify that an additional environment works from data alone and
   that the garden still opens in the existing renderer modes.
3. [in progress] Publish and verify the frontend, then record delivery evidence.

Acceptance. Environment names, geometry, stations, and introductory terrain
positions come from data. The mission API accepts environment IDs and stores
each environment's progress independently. The office continues to open
without the API.

Design. The Game Panel and DESIGN.md anchor the controls. The office content
uses a central aisle, desks, and workstations as landmarks. Reuse Babylon.js
for rendering and keep validation, navigation, and mission handling in C#.

Release, RulesSmoke, the garden and mission HTTP suites, and the Chromium
environment suite passed locally. Chromium injected a new atelier scene into
the catalog and opened it without code changes. The garden camera and
preference regressions also passed.

Next action. Publish the frontend and repeat the environment scenario against
the live build. The backend suite uses a disposable local API.
