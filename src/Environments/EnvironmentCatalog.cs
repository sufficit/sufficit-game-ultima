using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ultima.Environments;

public sealed record EnvironmentCatalog(string DefaultEnvironment, EnvironmentDefinition[] Environments)
{
    public static async Task<EnvironmentCatalog> LoadAsync(HttpClient http, CancellationToken token)
    {
        var catalog = await http.GetFromJsonAsync("environments.json", EnvironmentJson.Default.EnvironmentCatalog, token);
        if (catalog?.Environments is not { Length: >= 1 and <= 100 } ||
            catalog.Environments.Any(environment => environment is null || !environment.IsValid()) ||
            catalog.Environments.Select(environment => environment.Id).Distinct().Count() != catalog.Environments.Length ||
            !catalog.Environments.Any(environment => environment.Id == catalog.DefaultEnvironment && environment.Terrain is not null))
            throw new InvalidDataException("Invalid environment catalog.");
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
public sealed record SceneBox(double X, double Y, double Z, double Width, double Height, double Depth, string Color, string? Station);
public sealed record SceneSettings(SceneCamera Camera, string Background, SceneStation[] Stations, SceneBox[] Boxes)
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

    internal bool IsValid() => Camera is { Radius: >= 1 and <= 100, Beta: > 0 and < 3.14 } &&
        new[] { Camera.Alpha, Camera.TargetX, Camera.TargetY, Camera.TargetZ }.All(value => Math.Abs(value) <= 500) &&
        IsColor(Background) && Stations is { Length: <= 32 } && Boxes is { Length: >= 1 and <= 512 } &&
        Stations.All(station => station is not null && EnvironmentDefinition.ValidId(station.Id) &&
            !string.IsNullOrWhiteSpace(station.Name) && station.Name.Length <= 80) &&
        Stations.Select(station => station.Id).Distinct().Count() == Stations.Length &&
        Boxes.All(box => box is not null && box.Width is > 0 and <= 500 && box.Height is > 0 and <= 500 &&
            box.Depth is > 0 and <= 500 && Math.Abs(box.X) <= 500 && Math.Abs(box.Y) <= 500 && Math.Abs(box.Z) <= 500 &&
            IsColor(box.Color) && (box.Station is null || Stations.Any(station => station.Id == box.Station)));

    private static bool IsColor(string? color) => color is { Length: 7 } && Hex.IsMatch(color);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EnvironmentCatalog))]
internal sealed partial class EnvironmentJson : JsonSerializerContext;
