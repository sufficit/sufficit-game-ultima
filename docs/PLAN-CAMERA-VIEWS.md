# Shared camera perspectives

1. [complete] Confirm the Garden orbit and interior follow camera contracts.
2. [complete] Add a shared C# perspective cycle and map the browser inputs.
3. [in progress] Verify all perspectives, input edges and existing camera behavior,
   then publish and validate the hosted client.

Acceptance. Middle click, V, R3, and the camera button switch immediately
between rear third person, first person, and front third person. The avatar
remains visible in both third-person views. Existing zoom and collision
behavior stay available when returning to the rear view.

Release publish and RulesSmoke passed. Chromium verified the three camera
positions, model visibility, all four controls and the held R3 edge in
both garden renderer modes. The camera streaming and office regressions passed.

Next action. Publish and validate the perspectives on GitHub Pages.
