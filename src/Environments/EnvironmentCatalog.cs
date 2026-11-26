using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Ultima.Models.Core;

namespace Ultima.Environments;

public sealed record EnvironmentCatalog(string DefaultEnvironment, EnvironmentDefinition[] Environments, HumanoidProfile? PlayerBody = null)
{
    public static async Task<EnvironmentCatalog> LoadAsync(HttpClient http, CancellationToken token)
    {
        var catalog = await http.GetFromJsonAsync("environments.json", EnvironmentJson.Default.EnvironmentCatalog, token);
        if (catalog?.Environments is not { Length: >= 1 and <= 100 } ||
            catalog.Environments.Any(environment => environment is null || !environment.IsValid()) ||
            catalog.Environments.Select(environment => environment.Id).Distinct().Count() != catalog.Environments.Length ||
            !catalog.Environments.Any(environment => environment.Id == catalog.DefaultEnvironment && environment.Terrain is not null))
            throw new InvalidDataException("Invalid environment catalog.");
        try { _ = Humanoid.ResolveBody(catalog.PlayerBody); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid player body profile.", error); }
        return catalog;
    }
}

public sealed record EnvironmentDefinition(string Id, string Name, string Description,
    TerrainSettings? Terrain, SceneSettings? Scene)
{
    internal bool IsValid() =>
        ValidId(Id) && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 &&
        (Terrain is not null ? Scene is null && Terrain.IsValid() : Scene?.IsValid() == true);

    internal static bool ValidId(string? value) => value is { Length: >= 1 and <= 64 } &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '_');
}

public sealed record TerrainSettings(double HalfSize, SpawnPoint Spawn, MapPoint Guide, MapPoint Wanderer)
{
    internal bool IsValid() => HalfSize is >= 32 and <= 400 && Spawn is not null && Guide is not null && Wanderer is not null &&
        new[] { Spawn.X, Spawn.Z, Guide.X, Guide.Z, Wanderer.X, Wanderer.Z }.All(coordinate => Math.Abs(coordinate) <= HalfSize) &&
        Math.Abs(Spawn.Ry) <= Math.Tau;
}

public sealed record MapPoint(double X, double Z);
public sealed record SpawnPoint(double X, double Z, double Ry);
public sealed record SceneStation(string Id, string Name);
public sealed record SceneCamera(double Alpha, double Beta, double Radius, double TargetX, double TargetY, double TargetZ);
public sealed record SceneBox(double X, double Y, double Z, double Width, double Height, double Depth,
    string Color, string? Station, string? Material = null, bool Solid = false);
public sealed record SceneWalkSettings(double X, double Z, double Yaw, double MinX, double MaxX, double MinZ, double MaxZ)
{
    internal bool IsValid() => new[] { X, Z, Yaw, MinX, MaxX, MinZ, MaxZ }.All(value => Math.Abs(value) <= 500) &&
        X > MinX + .26 && X < MaxX - .26 && Z > MinZ + .26 && Z < MaxZ - .26;
}
public sealed record SceneSign(string Text, double X, double Y, double Z, double Width, double Height);
public sealed record SceneLight(double X, double Y, double Z, double Intensity);
public sealed record SceneSettings(SceneCamera Camera, string Background, SceneStation[] Stations, SceneBox[] Boxes,
    SceneWalkSettings? Walk = null, SceneSign[]? Signs = null, SceneLight[]? Lights = null)
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

    internal bool IsValid() => Camera is { Radius: >= 1 and <= 100, Beta: > 0 and < 3.14 } &&
        new[] { Camera.Alpha, Camera.TargetX, Camera.TargetY, Camera.TargetZ }.All(value => Math.Abs(value) <= 500) &&
        IsColor(Background) && (Walk is null || Walk.IsValid()) &&
        (Signs is null || Signs.Length <= 24 && Signs.All(sign => sign is not null && sign.Text is { Length: > 0 and <= 64 } &&
            At(sign.X, sign.Y, sign.Z) && sign.Width is > 0 and <= 16 && sign.Height is > 0 and <= 8)) &&
        (Lights is null || Lights.Length <= 12 && Lights.All(light => light is not null &&
            At(light.X, light.Y, light.Z) && light.Intensity is > 0 and <= 4)) &&
        Stations is { Length: <= 32 } && Boxes is { Length: >= 1 and <= 512 } &&
        Stations.All(station => station is not null && EnvironmentDefinition.ValidId(station.Id) &&
            !string.IsNullOrWhiteSpace(station.Name) && station.Name.Length <= 80) &&
        Stations.Select(station => station.Id).Distinct().Count() == Stations.Length &&
        Boxes.All(box => box is not null && box.Width is > 0 and <= 500 && box.Height is > 0 and <= 500 &&
            box.Depth is > 0 and <= 500 && Math.Abs(box.X) <= 500 && Math.Abs(box.Y) <= 500 && Math.Abs(box.Z) <= 500 &&
            IsColor(box.Color) && box.Material is (null or "wood" or "carpet" or "stone" or "glass" or "light" or "screen") && (box.Station is null || Stations.Any(station => station.Id == box.Station)));

    private static bool At(double x, double y, double z) => Math.Abs(x) <= 500 && Math.Abs(y) <= 500 && Math.Abs(z) <= 500;

    private static bool IsColor(string? color) => color is { Length: 7 } && Hex.IsMatch(color);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EnvironmentCatalog))]
internal sealed partial class EnvironmentJson : JsonSerializerContext;
