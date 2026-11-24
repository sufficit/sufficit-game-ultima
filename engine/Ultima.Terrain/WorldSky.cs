namespace Ultima.Terrain;

internal static class WorldSky
{
    private const double Radians = Math.PI / 180;
    private const double GreenwichLatitude = 51.4779 * Radians;
    private const double OrbitalEpoch = 946598400000; // 1999-12-31 00:00 UTC

    internal readonly record struct Direction(double East, double Up, double North);
    internal readonly record struct Sample(Direction Sun, Direction Moon, double Illumination, int Phase,
        double Daylight, double DirectSun);

    // Elementos orbitais de baixa precisão e termos dominantes da Lua:
    // https://stjarnhimlen.se/comp/ppcomp.html
    internal static Sample At(double unixMilliseconds)
    {
        var day = (unixMilliseconds - OrbitalEpoch) / 86400000;
        var obliquity = Angle(23.4393 - 3.563e-7 * day);
        var sunPerihelion = Angle(282.9404 + 4.70935e-5 * day);
        var sunAnomaly = Angle(356.047 + .9856002585 * day);
        var sunLongitude = sunPerihelion + TrueAnomaly(sunAnomaly, .016709 - 1.151e-9 * day);
        var lunarNode = Angle(125.1228 - .0529538083 * day);
        var lunarPerigee = Angle(318.0634 + .1643573223 * day);
        var lunarAnomaly = Angle(115.3654 + 13.0649929509 * day);
        var meanElongation = lunarNode + lunarPerigee + lunarAnomaly - sunPerihelion - sunAnomaly;
        var argumentLatitude = lunarPerigee + lunarAnomaly;
        var trueLatitudeArgument = lunarPerigee + TrueAnomaly(lunarAnomaly, .0549);
        var (lunarSin, lunarCos) = Math.SinCos(trueLatitudeArgument);
        const double inclination = 5.1454 * Radians;
        var lunarLongitude = lunarNode + Math.Atan2(lunarSin * Math.Cos(inclination), lunarCos)
            + Radians * (-1.274 * Math.Sin(lunarAnomaly - 2 * meanElongation)
                + .658 * Math.Sin(2 * meanElongation) - .186 * Math.Sin(sunAnomaly));
        var lunarLatitude = Math.Asin(lunarSin * Math.Sin(inclination))
            - .173 * Radians * Math.Sin(argumentLatitude - 2 * meanElongation);

        var sidereal = Angle(280.46061837 + 360.98564736629 * (day - 1.5));
        var (obliquitySin, obliquityCos) = Math.SinCos(obliquity);
        var sun = Horizon(sunLongitude, 0, sidereal, obliquitySin, obliquityCos);
        var moon = Horizon(lunarLongitude, lunarLatitude, sidereal, obliquitySin, obliquityCos);
        // Paralaxe lunar aproximada, reescalada para devolver uma direção unitária.
        var moonUp = moon.Up - 1 / 60.27;
        var length = Math.Sqrt(moon.East * moon.East + moonUp * moonUp + moon.North * moon.North);
        moon = new(moon.East / length, moonUp / length, moon.North / length);

        var elongation = (lunarLongitude - sunLongitude) % Math.Tau;
        if (elongation < 0) elongation += Math.Tau;
        var illumination = (1 - Math.Cos(elongation) * Math.Cos(lunarLatitude)) / 2;
        var phase = (int)Math.Floor(elongation / (Math.PI / 4) + .5) % 8;
        return new(sun, moon, illumination, phase, Smooth(-.21, .12, sun.Up), Smooth(-.015, .28, sun.Up));
    }

    private static Direction Horizon(double longitude, double latitude, double sidereal,
        double obliquitySin, double obliquityCos)
    {
        var (sinLongitude, cosLongitude) = Math.SinCos(longitude);
        var (sinLatitude, cosLatitude) = Math.SinCos(latitude);
        var x = cosLatitude * cosLongitude;
        var y = cosLatitude * sinLongitude;
        var equatorialY = y * obliquityCos - sinLatitude * obliquitySin;
        var equatorialZ = y * obliquitySin + sinLatitude * obliquityCos;
        var (sinSidereal, cosSidereal) = Math.SinCos(sidereal);
        var (sinObserver, cosObserver) = Math.SinCos(GreenwichLatitude);
        var meridian = x * cosSidereal + equatorialY * sinSidereal;
        return new(equatorialY * cosSidereal - x * sinSidereal,
            meridian * cosObserver + equatorialZ * sinObserver,
            equatorialZ * cosObserver - meridian * sinObserver);
    }

    private static double TrueAnomaly(double anomaly, double eccentricity)
    {
        var (sin, cos) = Math.SinCos(anomaly);
        var eccentric = anomaly + eccentricity * sin * (1 + eccentricity * cos);
        return Math.Atan2(Math.Sqrt(1 - eccentricity * eccentricity) * Math.Sin(eccentric),
            Math.Cos(eccentric) - eccentricity);
    }

    private static double Angle(double degrees) => degrees % 360 * Radians;

    private static double Smooth(double min, double max, double value)
    {
        var t = Math.Clamp((value - min) / (max - min), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
