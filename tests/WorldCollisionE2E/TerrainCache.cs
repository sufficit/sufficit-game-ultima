using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal static class TerrainCache
{
    private const string CacheDirectory = """
        const root = await navigator.storage.getDirectory();
        const storage = await root.getDirectoryHandle('ultima-assets');
        const key = await game.assets.digest(['terrain-mesh-v1', 0, 0]);
        """;

    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 800, Height = 450 } });
        var errors = TrackErrors(page);
        var timeout = LoadTimeoutMs(120);

        async Task<string?> ReadyAsync()
        {
            var build = await CheckBuildAsync(page);
            await WaitAsync(async () => (await ReadAsync<MapState?>(page,
                "() => window.game?.build?.state?.()"))?.Chunks >= 25, "os chunks não carregaram", timeout);
            Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "modo do renderizador", Mode);
            return build;
        }

        await page.GotoAsync(GameUrl(forceWebGl: true));
        var build = await ReadyAsync();
        await CheckSourceAsync(page, "generated", "a visita fria gera a malha");
        await WaitAsync(() => RenderAsync<bool>(page, $$"""
            async () => {
                {{CacheDirectory}}
                try { return !!(await storage.getFileHandle(key)); }
                catch (e) { if (e.name === 'NotFoundError') return false; throw e; }
            }
            """), "a visita fria precisa persistir a malha por build e chunk", timeout);
        await WaitAsync(async () =>
        {
            var models = await ReadAsync<ModelsState?>(page, "() => game.models.status()");
            return models is { Queue: 0, Loaded.Length: >= 6 };
        }, "a fila de modelos não drenou", timeout);
        var budget = await page.EvaluateAsync<double>("async () => (await game.perf.report()).budgetMs");
        Check(budget == 4, "orçamento de renderização de 4 ms", budget);

        await page.ReloadAsync();
        await ReadyAsync();
        await CheckSourceAsync(page, "opfs", "a revisita precisa usar a malha persistida");
        var original = await SignatureAsync(page);
        await page.EvaluateAsync("() => game.applyWorldOp({ kind: 'sculpt', x: 0, z: 0, r: 2, dh: -2 })");
        await CheckSourceAsync(page, "generated", "edição gera uma malha nova");
        var edited = await SignatureAsync(page);
        Check(edited.CenterHeight < original.CenterHeight - 1.9, "a edição não pode usar a malha base", new { original, edited });
        await page.EvaluateAsync("() => game.setWorldEdits([], 2)");
        var restored = await SignatureAsync(page);
        Check(restored == original, "remover a edição restaura a malha base", new { original, restored });

        await RenderAsync<JsonElement>(page, $$"""
            async () => {
                {{CacheDirectory}}
                const handle = await storage.getFileHandle(key);
                const bytes = new Uint8Array(await (await handle.getFile()).arrayBuffer());
                bytes[bytes.length - 1] ^= 1;
                const writable = await handle.createWritable();
                await writable.write(bytes);
                await writable.close();
            }
            """);
        await page.ReloadAsync();
        await ReadyAsync();
        await CheckSourceAsync(page, "generated", "uma falha de integridade deve regenerar a malha");
        var recovered = await SignatureAsync(page);
        Check(recovered == original, "a malha regenerada coincide com a original", new { original, recovered });
        Check(errors.IsEmpty, "erros no navegador", errors);
        Log(new { build, mode = Mode, warm = "opfs", edited = "generated", corrupted = "recovered", original, budget });
    }

    private static async Task CheckSourceAsync(IPage page, string source, string description)
    {
        var chunk = await ReadAsync<ChunkSnapshot>(page, "() => game.debugChunk(0, 0)");
        Check(chunk.MeshSource == source, description, chunk);
    }

    private static Task<MeshSignature> SignatureAsync(IPage page) => RenderAsync<MeshSignature>(page, """
        () => {
            const mesh = BABYLON.EngineStore.LastCreatedScene.getMeshByName('ground-0,0');
            const positions = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
            return { positionCount: positions.length, centerHeight: positions[3 * (65 * 32 + 32) + 1] };
        }
        """);

    private sealed record MeshSignature(int PositionCount, double CenterHeight);
}

internal sealed record ChunkSnapshot(string MeshSource, string TerrainEngine);
