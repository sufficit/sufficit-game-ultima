# Shared room presence across environments

## Investigation

The walkable office loaded the common renderer without starting the WebRTC
room. Its render loop skipped remote-avatar updates. Indoor poses also had
no procedural terrain bounds, so the terrain clamp produced invalid coordinates.

## Changes

- Initialize the existing room transport in the garden and every walkable
  authored scene. Navigation keeps the room query and the tab's peer identity.
- Tag WebRTC poses with the sending environment. Add an avatar after a valid
  pose arrives from the local scene, and remove it on transition or disconnect.
  Older unscoped poses belong to the default garden.
- Use the shared C# avatar simulation indoors and apply the configured scene
  bounds. Register the signed remote avatar model in the office.

## Validation and delivery

- Passed 243 C# smoke checks, 11 Node tests, six server integration scenarios,
  and the Release build.
- Two independent Chromium clients followed the same room invitation from
  the garden to the office. They saw each other and movement in real time.
  Leaving for the garden removed the office avatar. The regression passed
  in worker and main-thread modes, and the office scenario passed in both modes.
- Repeated the two-client worker test on public build
  `ad83a9c8f2997c16a11a2eb0a120ca5b8d23cccc` with an isolated loopback
  signaling API. [GitHub Pages run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45887534900)
