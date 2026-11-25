# Interior player view

1. [complete] Identify the panel sizing and first-person camera. Use the garden
   canvas and HUD as the design reference.
2. [complete] Give walkable environments a viewport-filling canvas, the
   signed player model and an obstacle-aware third-person camera.
3. [in progress] Verify viewport, avatar, walking, camera and mission UI in
   Chromium, then publish and verify the hosted client.

Acceptance. The office fills the browser viewport on desktop and mobile. The
player uses the garden avatar and follows the existing walking controls.
The follow camera stays within the interior and the mission UI remains usable.

Release publish, RulesSmoke and the office scenario passed locally. The
browser confirmed the signed avatar, viewport dimensions on desktop and
mobile, wall-aware camera, walking route and mission panel.

Next action. Publish the verified build and rerun the browser scenario on Pages.
