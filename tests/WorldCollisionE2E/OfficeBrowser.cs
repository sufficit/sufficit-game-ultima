using Microsoft.Playwright;
using static BrowserTest;

internal static class OfficeBrowser
{
    internal static async Task RunAsync(IBrowser browser)
    {
        const string testKey = "synthetic-browser-key-office-verification";
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1360, Height = 860 } });
        var errors = TrackErrors(page);
        var requested = 0;
        var allowBoard = false;
        await page.RouteAsync("**/v1/game/environments/office/missions", async route =>
        {
            if (!allowBoard)
            {
                await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" });
                return;
            }
            Check(route.Request.Headers.TryGetValue("authorization", out var authorization) &&
                authorization == "Bearer " + testKey, "The office sends its bearer key in the header.");
            var count = Math.Min(Interlocked.Increment(ref requested), 2);
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = $$"""
                    {"missions":[{"id":"daily-review","title":"Revisar fila","station":"review",
                    "complete":{{(count == 2 ? "true" : "false")}},
                    "lastEvent":{"id":"job-1","kind":"task.completed","target":"daily-summary","agent":"Agente de revisão"},
                    "objectives":[{"kind":"task.completed","target":"daily-summary","count":{{count}},"required":2}]}]}
                    """
            });
        });

        await page.RouteAsync("**/environments.json", async route =>
        {
            var response = await route.FetchAsync();
            var catalog = System.Text.Json.Nodes.JsonNode.Parse(await response.TextAsync())!;
            catalog["environments"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
                {"id":"atelier","name":"Ateliê de teste","description":"Ambiente definido por dados",
                 "scene":{"camera":{"alpha":-1.5,"beta":1,"radius":12,"targetX":0,"targetY":0,"targetZ":0},
                 "background":"#39424C","stations":[{"id":"bench","name":"Bancada"}],
                 "boxes":[{"x":0,"y":-0.1,"z":0,"width":6,"height":0.2,"depth":6,"color":"#52616E","station":"bench"}]}}
                """));
            await route.FulfillAsync(new() { Response = response, Body = catalog.ToJsonString() });
        });

        await page.GotoAsync(GameUrl(forceWebGl: true), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await Button("Abrir painel do jogo").ClickAsync();
        await Button("Ambientes").ClickAsync();
        await Button("Ateliê de teste").ClickAsync();
        await Button("Bancada").WaitForAsync();
        await WaitAsync(async () => (await page.EvaluateAsync<string?>("async () => await game.coreId()")) is not null &&
            await RenderAsync<bool>(page, """
                () => !!BABYLON.EngineStore.LastCreatedScene?.meshes.some(mesh => mesh.name === "environment-piece")
                """),
            "An additional scene loaded from the catalog alone.");
        await Button("Trocar ambiente").ClickAsync();
        await Button("Escritório").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Escritório", Exact = true }).WaitForAsync();
        await WaitAsync(async () => (await page.EvaluateAsync<string?>("async () => await game.coreId()")) is not null &&
            await RenderAsync<bool>(page, """
                () => {
                    const scene = BABYLON.EngineStore.LastCreatedScene;
                    return !!scene && scene.activeCamera?.getClassName() === 'ArcRotateCamera' &&
                        scene.getTotalVertices() > 1500 && !scene.meshes.some(mesh => mesh.name.startsWith("ground-"));
                }
                """), "The main game engine loads office geometry independently of its API.");
        Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "The office uses the requested renderer.");
        var canvas = page.Locator(".environment__scene canvas");
        await WaitAsync(() => RenderAsync<bool>(page, """
            () => {
                const avatar = BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player');
                return avatar?.metadata?.modelId === 'character.villager' &&
                    avatar.metadata.category === 'character' && avatar.getChildMeshes().length > 10;
            }
            """), "The signed garden player model is visible in the office.");
        Check(await FillsViewportAsync(), "The office fills the desktop viewport.");
        Check(await RenderAsync<bool>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                return ['upper-body', 'hip-left', 'knee-left', 'ankle-left', 'elbow-right', 'wrist-right'].every(
                    joint => !!scene.getTransformNodeByName('player-' + joint));
            }
            """), "The office avatar has the articulated garden joints.");
        if (Environment.GetEnvironmentVariable("ULTIMA_INTERIOR_CAPTURE") is { Length: > 0 } interior)
            await page.ScreenshotAsync(new() { Path = interior });
        await canvas.FocusAsync();
        await WalkUntilAsync("s", pose => pose.Z > 7.43, "The player can reach the reception wall.");
        var nearWall = await RenderAsync<CameraPose>(page, """
            () => {
                const { x, y, z } = BABYLON.EngineStore.LastCreatedScene.activeCamera.position;
                return { x, y, z };
            }
            """);
        Check(nearWall.Z <= 7.74 && nearWall.Y > .3, "The follow camera stays inside at the reception wall.", nearWall);
        await RenderAsync<object?>(page, """
            () => {
                const scene = BABYLON.EngineStore.LastCreatedScene;
                const knee = scene.getTransformNodeByName('player-knee-left');
                const ankle = scene.getTransformNodeByName('player-ankle-left');
                globalThis.officeJointSample = { min: knee.rotation.x, max: knee.rotation.x, ankle: false };
                const sample = globalThis.officeJointSample;
                scene.onAfterRenderObservable.add(() => {
                    sample.min = Math.min(sample.min, knee.rotation.x);
                    sample.max = Math.max(sample.max, knee.rotation.x);
                    sample.ankle ||= Math.abs(ankle.rotationQuaternion.x) > .05;
                });
            }
            """);
        await WalkUntilAsync("w", pose => pose.Z < -.35, "Reception connects to the workspace.");
        Check(await RenderAsync<bool>(page, "() => officeJointSample.max - officeJointSample.min > .15 && officeJointSample.ankle"),
            "Knees flex and the ankle rotates during the shared walk cycle.");
        var aisle = await PoseAsync();
        Check(Math.Abs(aisle.X) < .1 && aisle.Z > -1.02, "The player is aligned with the meeting room door.", aisle);
        await WalkUntilAsync("a", pose => pose.X > 3.86, "The player can enter the meeting room through its doorway.");
        await WalkUntilAsync("w", pose => pose.Z < -4, "The player can walk alongside the meeting table.");
        await page.Keyboard.DownAsync("d");
        try { await Task.Delay(1000); }
        finally { await page.Keyboard.UpAsync("d"); }
        var atGlass = await PoseAsync();
        Check(atGlass.X is > 3.60 and < 3.96, "The glass wall stops the player.", atGlass);
        var beforeLook = await RenderAsync<double>(page, "() => BABYLON.EngineStore.LastCreatedScene.activeCamera.alpha");
        var region = await canvas.BoundingBoxAsync() ?? throw new InvalidOperationException("Missing office canvas.");
        await page.Mouse.MoveAsync(region.X + region.Width * .5f, region.Y + region.Height * .5f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(region.X + region.Width * .5f + 100, region.Y + region.Height * .5f, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await WaitAsync(async () =>
        {
            var after = await RenderAsync<double>(page, "() => BABYLON.EngineStore.LastCreatedScene.activeCamera.alpha");
            return Math.Abs(Math.IEEERemainder(after - beforeLook, Math.Tau)) > .20;
        }, "Dragging turns the camera.");
        await Button("Missões").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revisão" }).ClickAsync();
        Check(await Button("Revisão").GetAttributeAsync("aria-pressed") == "true", "Station selection is visible.");
        var api = new UriBuilder(page.Url) { Path = "/", Query = "" };
        await page.GetByLabel("Endereço da API").FillAsync(api.Uri.AbsoluteUri);
        var key = page.GetByLabel("Chave de missões");
        await key.FillAsync(testKey);
        await Button("Conectar").ClickAsync();
        await page.GetByText("A API de missões está indisponível neste servidor.").WaitForAsync();

        allowBoard = true;
        await key.FillAsync(testKey);
        await Button("Conectar").ClickAsync();
        await page.GetByText("Revisar fila", new() { Exact = true }).WaitForAsync();
        await page.GetByText("1 / 2", new() { Exact = true }).WaitForAsync();
        await page.GetByText("Concluída", new() { Exact = true }).WaitForAsync(new() { Timeout = 15_000 });
        await Button("Operações").ClickAsync();
        await page.GetByText("Nenhuma missão nesta estação.").WaitForAsync();
        await Button("Revisão").ClickAsync();
        await page.GetByText("Agente de revisão", new() { Exact = false }).WaitForAsync();

        await page.SetViewportSizeAsync(390, 844);
        await WaitAsync(FillsViewportAsync, "The office fills the mobile viewport.");
        if (Environment.GetEnvironmentVariable("ULTIMA_OFFICE_CAPTURE") is { Length: > 0 } capture)
        {
            await page.SetViewportSizeAsync(1360, 860);
            await page.ScreenshotAsync(new() { Path = capture });
        }
        await Button("Desconectar").ClickAsync();
        await key.WaitForAsync();
        Check(await key.InputValueAsync() == "", "The key is cleared after disconnection.");
        await Button("Trocar ambiente").ClickAsync();
        await Button("Jardim").ClickAsync();
        await page.Locator("#game-canvas").WaitForAsync();
        await WaitAsync(() => page.EvaluateAsync<bool>("async () => !!(await window.game?.playerState?.())"),
            "The terrain profile loaded after returning to the garden.");
        Check(errors.IsEmpty, "Browser page errors", errors);
        Log(new { build, office = true, board = true, desktop = true, mobile = true, atGlass });

        Task<CameraPose> PoseAsync() => RenderAsync<CameraPose>(page, """
            () => {
                const { x, y, z } = BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position;
                return { x, y, z };
            }
            """);

        Task<bool> FillsViewportAsync() => page.EvaluateAsync<bool>("""
            () => {
                const canvas = document.querySelector('.environment__scene canvas').getBoundingClientRect();
                return canvas.x === 0 && canvas.y === 0 &&
                    Math.abs(canvas.width - innerWidth) < 1 && Math.abs(canvas.height - innerHeight) < 1;
            }
            """);

        async Task WalkUntilAsync(string keyName, Func<CameraPose, bool> reached, string description)
        {
            await page.Keyboard.DownAsync(keyName);
            try { await WaitAsync(async () => reached(await PoseAsync()), description, 16_000, 25); }
            finally { await page.Keyboard.UpAsync(keyName); }
        }

        ILocator Button(string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    private sealed record CameraPose(double X, double Y, double Z);
}
