using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ultima.Models.Core;

namespace Ultima.Server;

internal sealed class CharacterProfiles(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    internal static bool IsKey(string? key) => key is { Length: 64 } && key.All(char.IsAsciiHexDigitLower);

    // Store a hash so a filesystem path never contains the bearer credential.
    private string PathFor(string key) => Path.Combine(directory,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(key))) + ".json");

    internal HumanoidProfile? Read(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? JsonSerializer.Deserialize<HumanoidProfile>(File.ReadAllBytes(path), Json) : null;
    }

    internal static JsonObject Reply(HumanoidProfile? profile) => new()
    {
        ["profile"] = JsonSerializer.SerializeToNode(profile, Json)
    };

    internal (int, JsonObject) Update(string key, JsonObject patch)
    {
        var existing = Read(key);
        try
        {
            var merged = JsonSerializer.SerializeToNode(existing ?? new HumanoidProfile(), Json)!.AsObject();
            foreach (var (field, value) in patch)
            {
                if (field is "skin" or "eyes" or "hair" && value is not null &&
                    (value is not JsonObject rgb || !rgb.ContainsKey("red") || !rgb.ContainsKey("green") || !rgb.ContainsKey("blue")))
                    return (400, J.Err("invalid_body_profile"));
                merged[field] = value?.DeepClone();
            }

            var profile = merged.Deserialize<HumanoidProfile>(Json)!;
            _ = Humanoid.ResolveBody(profile);
            if (profile != existing) Save(key, profile);
            return (200, Reply(profile));
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return (400, J.Err("invalid_body_profile"));
        }
    }

    // GameWorld serializes writes, including writes from two sessions for the same character.
    private void Save(string key, HumanoidProfile profile)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(key);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, profile, Json);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
