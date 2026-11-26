# One engine for the garden and office

1. [complete] Trace the scene data, input, simulation, and mission interfaces.
2. [complete] Load office geometry through the game engine and retire its separate controller.
3. [complete] Verify shared movement, foot support, collision, camera modes and mission access in both environments.
4. [in progress] Publish, verify the live build and archive this plan.

Acceptance. The selector opens both places through the same game boot and
simulation. Office geometry comes from its catalog entry. Its mission board
remains accessible. Test worker and main-thread paths.

Local checks passed. Release, RulesSmoke, camera views and camera streaming
in both renderer modes, the articulated step, and office traversal and
mission board interaction with a test API.

Next action. Push main and verify camera views and office traversal on Pages.
