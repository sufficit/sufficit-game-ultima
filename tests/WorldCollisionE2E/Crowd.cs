using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

internal static class Crowd
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var counts = (Environment.GetEnvironmentVariable("ULTIMA_COUNTS") ?? "100,300")
            .Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture));
        foreach (var count in counts)
        {
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 720 } });
            try { await CheckCrowdAsync(page, count); }
            finally { await page.CloseAsync(); }
        }
    }

    private static async Task CheckCrowdAsync(IPage page, int count)
    {
        var errors = TrackErrors(page);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && Regex.IsMatch(message.Text, "shader|program|vertex.*error", RegexOptions.IgnoreCase))
                errors.Enqueue(message.Text);
        };
        var url = new UriBuilder(GameUrl());
        url.Query += "&crowd=" + count.ToString(CultureInfo.InvariantCulture);
        await page.GotoAsync(url.Uri.AbsoluteUri);
        var build = await CheckBuildAsync(page);
        Task<CrowdState?> StatusAsync() => ReadAsync<CrowdState?>(page, "() => window.game?.crowdStatus?.()");

        async Task<CrowdState> SampleAsync(Func<CrowdState, bool> condition)
        {
            CrowdState? status = null;
            await WaitAsync(async () =>
            {
                status = await StatusAsync();
                if (!string.IsNullOrEmpty(status?.Error)) throw new InvalidOperationException(status.Error);
                return status is not null && condition(status);
            }, "a multidão não atingiu o estado esperado", 20_000);
            return status!;
        }

        var baseline = await SampleAsync(status => status.Types is { Length: 2 } &&
            status.Types.All(type => type.Ready && type.Visible[0] > 0));
        Check(baseline.Requested == count && baseline.FrustumCulled > 0,
            "multidão com a quantidade pedida e descarte fora da câmera", baseline);
        await WaitAsync(async () => (await ReadAsync<ModelsState?>(page, "() => game.models.status()"))?.Queue == 0,
            "fila de assets não terminou", 10_000, 100);

        var sustained = new double[6];
        for (var i = 0; i < sustained.Length; i++)
        {
            await Task.Delay(500);
            sustained[i] = (await StatusAsync())?.Fps ?? 0;
        }
        Array.Sort(sustained);
        Check(sustained[0] > 0 && sustained.All(double.IsFinite), "seis amostras válidas de FPS", sustained);
        Check(baseline.Types.All(type => type.Vertices[1] < type.Vertices[0]), "LOD distante tem menos vértices", baseline);
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);

        // Menos pixels por personagem exercitam o segundo LOD.
        await page.SetViewportSizeAsync(720, 180);
        var small = await SampleAsync(status => status.Types.Any(type => type.Visible[1] > 0) &&
            status.Types.All(type => type.Ready && type.Time > baseline.Types[0].Time));
        var shared = await RenderAsync<SharedMeshes>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const high = scene.getMeshByName('crowd-high-0'), low = scene.getMeshByName('crowd-low-0');
                const mesh = scene.meshes.find(mesh => mesh.name.startsWith('crowd-') && mesh.isEnabled());
                return {
                    skeleton: high.skeleton === low.skeleton,
                    palette: high.bakedVertexAnimationManager.texture === low.bakedVertexAnimationManager.texture,
                    compiled: !!mesh?.subMeshes[0]?.effect?.isReady(),
                    renderer: scene.getEngine().getGlInfo()?.renderer
                };
            }
            """);
        Check(shared.Skeleton && shared.Palette && shared.Compiled, "esqueleto e paleta compartilhados com shader pronto", shared);

        await RenderAsync<JsonElement>(page, """
            () => { BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.x = 120; }
            """);
        var far = await SampleAsync(status => status.DistanceCulled == count);
        Check(far.Types.All(type => type.Visible.All(value => value == 0)), "descarta todos os avatares distantes", far);
        Check(errors.IsEmpty, "erros no navegador", errors);
        var fps = new { min = sustained[0], median = (sustained[2] + sustained[3]) / 2, max = sustained[5] };
        Log(new { build, count, mode = Mode, fps, baseline, small, far = far.DistanceCulled, shared });
    }

    private sealed record CrowdState(int Requested, int DistanceCulled, int FrustumCulled, double Fps, double Cutoff,
        CrowdType[] Types, string? Error);
    private sealed record CrowdType(bool Ready, double Time, int[] Visible, int[] Vertices);
    private sealed record SharedMeshes(bool Skeleton, bool Palette, bool Compiled, string? Renderer);
}
