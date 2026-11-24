using System.Runtime.InteropServices.JavaScript;

namespace Ultima.Terrain;

internal static partial class BrowserExports
{
    [JSExport]
    internal static double[] SampleSky(double unixMilliseconds)
    {
        var sky = WorldSky.At(unixMilliseconds);
        return [sky.Sun.East, sky.Sun.Up, sky.Sun.North, sky.Moon.East, sky.Moon.Up, sky.Moon.North,
            sky.Illumination, sky.Daylight, sky.DirectSun, sky.Phase];
    }

    [JSExport]
    internal static double[] WeatherProfile(string id)
    {
        var profile = WorldWeather.Find(id);
        return [profile.Clouds, profile.Rain, profile.Fog, profile.Wind];
    }
}
