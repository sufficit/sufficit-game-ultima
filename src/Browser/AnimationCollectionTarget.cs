using System.Text.Json.Serialization;

namespace Ultima.Browser;

internal sealed record AnimationCollectionTarget(CollectedResource Resource, GroundPoint? Point);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AnimationCollectionTarget))]
internal sealed partial class AnimationTargetJson : JsonSerializerContext;
