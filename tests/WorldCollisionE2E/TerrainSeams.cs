using Microsoft.Playwright;
using static BrowserTest;

internal static class TerrainSeams
{
    private const double DigX = 15.82;
    private const double DigZ = 16.05;
    private const int BaseVertices = 4225;
    private const int Samples = 128;

    internal static async Task RunAsync(IBrowser browser)
    {
        var procedural = Environment.GetEnvironmentVariable("ULTIMA_PROCEDURAL") == "1";
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
        if (procedural) await page.RouteAsync("**/terrain/*.webp*", route => route.AbortAsync());
        var errors = TrackErrors(page);
        await page.GotoAsync(GameUrl());
        var build = await CheckBuildAsync(page);
        await WaitAsync(async () =>
        {
            var world = await ReadAsync<TerrainState?>(page, """
                async () => {
                    if (!window.game?.weather?.status || !window.game?.models?.status) return null;
                    return { weather: await game.weather.status(), models: await game.models.status(), map: await game.build.state() };
                }
                """);
            return world is { Models.Queue: 0, Map.Chunks: >= 25 } && world.Weather?.Materials == (procedural ? 0 : 2);
        }, "o terreno detalhado não carregou", LoadTimeoutMs(100));
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
        var chunk = await ReadAsync<ChunkSnapshot>(page, "() => game.debugChunk(0, 0)");
        Check(chunk.TerrainEngine == "csharp-wasm", "terreno renderizado pelo motor C#", chunk);

        var probe = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "seam-probe.js"));
        var rays = (from radius in new[] { .54, .68, .82, .94 }
                    from i in Enumerable.Range(0, 48)
                    let angle = i * Math.PI / 24
                    select new[] { DigX + Math.Cos(angle) * radius, DigZ + Math.Sin(angle) * radius }).ToArray();
        async Task<Measurement> MeasureAsync()
        {
            var scan = await RenderAsync<Scan>(page, probe, rays);
            return new(ColorGap(scan.East, true), ColorGap(scan.North, false), scan.Vertices,
                Math.Max(GeometryGap(scan.East), GeometryGap(scan.North)),
                scan.Wall.Max(sample => Math.Abs(sample.Visible - sample.Simulated)));
        }

        var initial = await MeasureAsync();
        var cuts = Enumerable.Range(0, 6).Select(i => new { id = $"seam-dig-{i}", kind = "soil", x = DigX, z = DigZ }).ToArray();
        await page.EvaluateAsync("""
            cuts => {
                game.setCollectedResources(cuts);
                game.applyWorldOp({ kind: 'paint', mat: 'dirt', x: 16, z: 14, r: 4 });
            }
            """, cuts);
        await Task.Delay(350);
        var edited = await MeasureAsync();
        Log(new { build, mode = Mode, procedural, initial, edited });
        foreach (var sample in new[] { initial, edited })
        {
            Check(sample is { East: < 1, North: < 1 }, "o mesmo ponto deve ter a mesma cor nos dois chunks", sample);
            Check(sample.Vertices >= BaseVertices, "a malha deve resolver curvas em escala inferior a 0,67 m", sample);
            Check(sample.GeometryGap < .00001, "alturas e normais precisam coincidir na borda", sample);
        }
        Check(initial.Vertices == BaseVertices, "mantém a malha estável fora das edições", initial);
        Check(edited.WallError < .16, "a malha deve seguir a curvatura da parede escavada", edited);

        Task<int> OtherVerticesAsync() => RenderAsync<int>(page, """
            () => BABYLON.EngineStore.LastCreatedScene.getMeshByName('ground--1,0').getTotalVertices()
            """);
        await page.EvaluateAsync("() => game.setWorldEdits([{ kind: 'sculpt', x: -24, z: 4, r: 2, dh: -2 }], 1)");
        await Task.Delay(250);
        Check(await OtherVerticesAsync() > BaseVertices, "refina a escultura compartilhada");
        await page.EvaluateAsync("() => game.setWorldEdits([], 2)");
        await Task.Delay(250);
        Check(await OtherVerticesAsync() == BaseVertices, "volta à malha base ao remover a escultura");
        Check(errors.IsEmpty, "erros no navegador", errors);
    }

    private static double ColorGap(Boundary edge, bool alongX)
    {
        var sum = 0d;
        for (var i = 0; i < Samples; i++)
        {
            var t = (i + .5) / Samples;
            for (var channel = 0; channel < 3; channel++)
            {
                var center = Sample(edge.Center, alongX ? 1 : t, alongX ? t : 1, channel);
                var neighbor = Sample(edge.Neighbor, alongX ? 0 : t, alongX ? t : 0, channel);
                sum += Math.Abs(center - neighbor);
            }
        }
        return sum / (Samples * 3);
    }

    private static double Sample(PixelStrip strip, double u, double v, int channel)
    {
        var x = Math.Clamp((u * strip.UScale + strip.UOffset) * strip.Width - .5, 0, strip.Width - 1);
        var y = Math.Clamp((v * strip.VScale + strip.VOffset) * strip.Height - .5, 0, strip.Height - 1);
        var left = (int)x;
        var top = (int)y;
        var right = Math.Min(strip.Width - 1, left + 1);
        var bottom = Math.Min(strip.Height - 1, top + 1);
        byte Pixel(int col, int row) => strip.Pixels[((row - strip.Y) * strip.Stride + col - strip.X) * 4 + channel];
        var a = Pixel(left, top) + (x - left) * (Pixel(right, top) - Pixel(left, top));
        var b = Pixel(left, bottom) + (x - left) * (Pixel(right, bottom) - Pixel(left, bottom));
        return a + (y - top) * (b - a);
    }

    private static double GeometryGap(Boundary edge)
    {
        var neighbor = edge.NeighborEdge.ToDictionary(point => point.Location, point => point.Values);
        Check(edge.CenterEdge.Length == neighbor.Count, "as bordas têm topologias diferentes");
        var gap = 0d;
        foreach (var point in edge.CenterEdge)
        {
            if (!neighbor.TryGetValue(point.Location, out var other))
                throw new InvalidOperationException($"vértice da emenda sem par em {point.Location}");
            for (var axis = 0; axis < point.Values.Length; axis++)
                gap = Math.Max(gap, Math.Abs(point.Values[axis] - other[axis]));
        }
        return gap;
    }

    private sealed record TerrainState(MaterialState? Weather, ModelsState? Models, MapState? Map);
    private sealed record MaterialState(int Materials);
    private sealed record Scan(Boundary East, Boundary North, int Vertices, HeightPair[] Wall);
    private sealed record Boundary(PixelStrip Center, PixelStrip Neighbor, EdgePoint[] CenterEdge, EdgePoint[] NeighborEdge);
    private sealed record PixelStrip(int Width, int Height, double UScale, double VScale, double UOffset, double VOffset,
        int X, int Y, int Stride, byte[] Pixels);
    private sealed record EdgePoint(double Location, double[] Values);
    private sealed record HeightPair(double Visible, double Simulated);
    private sealed record Measurement(double East, double North, int Vertices, double GeometryGap, double WallError);
}
