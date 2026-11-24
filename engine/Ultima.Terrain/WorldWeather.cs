namespace Ultima.Terrain;

internal static class WorldWeather
{
    internal readonly record struct Profile(string Id, string Label, double Clouds, double Rain, double Fog, double Wind);

    internal static readonly Profile[] Profiles =
    [
        new("clear", "Céu limpo", 0, 0, .46, .06),
        new("cloudy", "Nublado", .68, 0, .6, .09),
        new("fog", "Neblina", .55, 0, 2.1, .035),
        new("rain", "Chuva", .88, 1, 1.09, .17),
        new("heavy-rain", "Chuva forte", 1, 1.5, 1.55, .24)
    ];

    internal static Profile Find(string? id)
    {
        foreach (var profile in Profiles)
            if (profile.Id == id) return profile;
        return Profiles[0];
    }
}
