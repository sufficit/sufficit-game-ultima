using System.Text.Json;
using Ultima.Models.Core;
using Ultima.Terrain;

// The skinned reference uses a higher shoulder profile than the legacy avatar.
var body = Humanoid.ResolveBody(new(ShoulderHeightRatio: .82));
var joints = PlayerAnatomy.Create("humano", body.Height, body);
Console.WriteLine(JsonSerializer.Serialize(joints, AnatomyJsonContext.Default.AnatomyJointArray));
