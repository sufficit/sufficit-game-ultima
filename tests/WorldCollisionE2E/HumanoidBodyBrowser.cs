using System.Text.Json.Nodes;
using Microsoft.Playwright;
using static BrowserTest;

internal static class HumanoidBodyBrowser
{
    internal static async Task RunAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 800 } });
        var errors = TrackErrors(page);
        await page.RouteAsync("**/environments.json", async route =>
        {
            var response = await route.FetchAsync();
            var catalog = JsonNode.Parse(await response.TextAsync())!.AsObject();
            catalog["playerBody"] = JsonNode.Parse("""
                {
                  "species":"homo-sapiens", "height":2.02, "mass":94, "build":1.17,
                  "legRatio":0.46, "faceWidth":1.12, "earScale":1.2,
                  "femurRatio":0.56, "armLengthRatio":0.4, "upperArmRatio":0.53, "shoulderHeightRatio":0.83,
                  "waistCircumference":1.38, "chestScale":1.04, "thighScale":1.4,
                  "calfScale":0.85, "upperArmScale":1.3, "forearmScale":0.9,
                  "skin":{"red":0.34,"green":0.19,"blue":0.12},
                  "eyes":{"red":0.15,"green":0.42,"blue":0.28},
                  "hair":{"red":0.09,"green":0.05,"blue":0.035}
                }
                """);
            await route.FulfillAsync(new() { Response = response, Body = catalog.ToJsonString() });
        });
        await page.GotoAsync(GameUrl(forceWebGl: true), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var build = await CheckBuildAsync(page);
        await VerifyProfileAsync();
        await page.Keyboard.PressAsync("v");
        await VerifyEyesAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Abrir painel do jogo" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Ambientes", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Escritório", Exact = true }).ClickAsync();
        await VerifyProfileAsync();

        var maxZ = 7.82 - await page.EvaluateAsync<double>("async () => (await game.playerBody()).colliderRadius");
        await page.Keyboard.DownAsync("s");
        try
        {
            await WaitAsync(async () => await ZAsync() > maxZ - .05, "The custom body reaches the reception wall.", 12_000);
            await Task.Delay(350);
            Check(await ZAsync() <= maxZ + .015, "The office sweep applies the custom body radius.");
        }
        finally { await page.Keyboard.UpAsync("s"); }
        await page.Keyboard.PressAsync("v");
        await VerifyEyesAsync();
        Check(errors.IsEmpty, "Humanoid profile loads without page errors.", errors);
        Log(new { build, mode = Mode, height = 2.02, garden = true, office = true, modelAndRig = true });

        Task<double> ZAsync() => page.EvaluateAsync<double>("async () => (await game.playerState()).z");

        async Task VerifyProfileAsync()
        {
            await WaitAsync(async () =>
                await page.EvaluateAsync<bool>("async () => !!(await window.game?.playerState?.()) && (await game.playerBody())?.height === 2.02"),
                "The engine resolves a custom human profile.");
            Check(await page.EvaluateAsync<string>("() => game.mode") == Mode, "Expected environment renderer.");
            await WaitAsync(() => RenderAsync<bool>(page, """
                () => {
                    const scene = BABYLON.EngineStore.LastCreatedScene;
                    const root = scene.getTransformNodeByName('player');
                    const hip = scene.getTransformNodeByName('player-hip-roll-left');
                    const skull = scene.getMeshByName('char-character.villager-head-player');
                    const ears = scene.getMeshByName('char-character.villager-ears-player');
                    const shoulder = scene.getTransformNodeByName('player-shoulder-left');
                    const knee = scene.getTransformNodeByName('player-knee-left');
                    const ankle = scene.getTransformNodeByName('player-ankle-left');
                    const elbow = scene.getTransformNodeByName('player-elbow-left');
                    const wrist = scene.getTransformNodeByName('player-wrist-left');
                    const thigh = scene.getMeshByName('player-thigh-left');
                    const shin = scene.getMeshByName('player-shin-left');
                    const torso = scene.getMeshByName('char-character.villager-torso-player');
                    const shoulders = scene.getMeshByName('char-character.villager-shoulders-player');
                    const bounds = shoulders?.getBoundingInfo().boundingBox;
                    return !!bounds && shoulders.parent.name === 'player-upper-body' &&
                        Math.abs(bounds.maximum.y - 2.02 * (.83 + 1.435 / 1.75 - .80518)) < .002 &&
                        Math.abs(bounds.minimum.y - 2.02 * (.83 + 1.324 / 1.75 - .80518)) < .002 &&
                        Math.abs(shoulder.metadata.modelOffset.x - 2.02 * .10067 * 1.17 * 1.04) < .001 &&
                        !!earMaterial() && root.metadata?.modelId === 'character.villager' &&
                        Math.abs(hip.position.x + 2.02 * .052 * 1.17) < .001 &&
                        Math.abs(shoulder.metadata.modelOffset.y + 2.02 * .83) < .001 &&
                        knee.metadata.flexion?.axisX === 1 &&
                        Math.abs(knee.metadata.flexion.maxRadians - 145 * Math.PI / 180) < .0001 &&
                        ankle.metadata.flexion?.axisX === -1 && elbow.metadata.flexion?.axisX === -1 &&
                        Math.abs(knee.position.y / (knee.position.y + ankle.position.y) - .56) < .001 &&
                        Math.abs(elbow.position.y / (elbow.position.y + wrist.position.y) - .53) < .001 &&
                        Math.abs(elbow.position.y + wrist.position.y + 2.02 * .4) < .001 &&
                        thigh.getBoundingInfo().boundingBox.maximum.x > shin.getBoundingInfo().boundingBox.maximum.x &&
                        Math.abs(torso.getBoundingInfo().boundingBox.maximum.x * Math.PI * 2 - 1.38) < .04;

                    function earMaterial() {
                        if (!ears || !skull) return false;
                        const color = skull.material.diffuseColor;
                        return Math.abs(color.r - .34) < .001 && Math.abs(color.g - .19) < .001 &&
                            Math.abs(color.b - .12) < .001;
                    }
                }
                """), "The signed model and joint positions use the custom profile.");
            await page.Locator("#game-canvas").FocusAsync();
        }

        async Task VerifyEyesAsync()
        {
            await WaitAsync(async () =>
                await page.EvaluateAsync<int>("async () => (await game.cameraState()).view") == 1 &&
                await RenderAsync<bool>(page, """
                    () => {
                        const scene = BABYLON.EngineStore.LastCreatedScene;
                        const root = scene.getTransformNodeByName('player');
                        return Math.abs(scene.activeCamera.position.y - root.position.y - 2.02 * .937) < .01;
                    }
                    """), "First person uses the custom eyes' height.");
        }
    }
}
