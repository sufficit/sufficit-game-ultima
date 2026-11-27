using System.Text.Json;
using Ultima.Models.Core;
using Ultima.Terrain;

var body = Humanoid.ResolveBody(null);
var joints = PlayerAnatomy.Create("humano", body.Height, body);
Console.WriteLine(JsonSerializer.Serialize(joints, AnatomyJsonContext.Default.AnatomyJointArray));
