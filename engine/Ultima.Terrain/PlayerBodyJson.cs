using System.Text.Json.Serialization;
using Ultima.Models.Core;

namespace Ultima.Terrain;

internal sealed record PlayerBodyFrame(HumanoidBody Body, AnatomyJoint[] Anatomy);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HumanoidProfile))]
[JsonSerializable(typeof(PlayerBodyFrame))]
internal sealed partial class PlayerBodyJson : JsonSerializerContext;
