using System.Text.Json.Serialization;
using Ultima.Models.Core;

namespace Ultima.Terrain;

// Startup packet shared by the worker and main-thread renderer. Frame updates carry pose angles.
internal sealed record PlayerBodyFrame(HumanoidBody Body, AnatomyJoint[] Anatomy);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HumanoidProfile))]
[JsonSerializable(typeof(PlayerBodyFrame))]
internal sealed partial class PlayerBodyJson : JsonSerializerContext;
