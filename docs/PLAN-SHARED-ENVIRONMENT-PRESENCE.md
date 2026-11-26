# Shared room presence across environments

1. [complete] Trace room identity, signaling, and remote avatars across navigation.
2. [complete] Preserve the room and scope remote avatars to the loaded environment.
3. [in progress] Verify two players across the garden and office, then publish and check the client.

Acceptance. Two players in the same room can see each other in the office.
Moving between environments keeps the room and hides avatars in the other
environment. Tests exercise the worker and main-thread renderers.

Next action. Exercise two Chromium players through both scenes and check the published build.
