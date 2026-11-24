using System.Text.Json;
using Microsoft.Playwright;
using static BrowserTest;

internal sealed partial class Backpack
{
    private static Task PlaceAsync(IPage page) => page.EvaluateAsync("op => game.applyWorldOp(op)", new
    {
        kind = "place", uid = FixtureId, asset = "prop.stump", x = -.85, z = 2, rot = 0, scale = 1
    });

    private static Task CollectAsync(IPage page, object entry) =>
        page.EvaluateAsync("entry => game.backpack.collect(entry)", entry);

    private static Task<bool?> EnabledAsync(IPage page) => RenderAsync<bool?>(page, """
        name => BABYLON.EngineStore.LastCreatedScene.getMeshByName(name)?.isEnabled()
        """, Mesh);

    private static Task<ScreenPoint> ProjectAsync(IPage page, Target point) => RenderAsync<ScreenPoint>(page, """
        ({name,x,z}) => {
            const s = BABYLON.EngineStore.LastCreatedScene;
            const engine = s.getEngine();
            const world = name ? s.getMeshByName(name).getBoundingInfo().boundingBox.centerWorld :
                s.pickWithRay(new BABYLON.Ray(new BABYLON.Vector3(x,100,z),new BABYLON.Vector3(0,-1,0)),m=>m.name.startsWith('ground-')).pickedPoint;
            const viewport = s.activeCamera.viewport.toGlobal(engine.getRenderWidth(),engine.getRenderHeight());
            const screen = BABYLON.Vector3.Project(world,BABYLON.Matrix.Identity(),s.getTransformMatrix(),viewport);
            const scale = engine.getHardwareScalingLevel();
            return {x:screen.x*scale,y:screen.y*scale,alt:world.y};
        }
        """, new { name = point.Name, x = point.X, z = point.Z });

    private static Task<Preview> PreviewAsync(IPage page) => RenderAsync<Preview>(page, """
        () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const active = scene.meshes.filter(mesh => mesh.isEnabled() && mesh.name.startsWith('collect-'));
            const ring = active.find(mesh => mesh.name === 'collect-ring-1');
            const center = ring?.getBoundingInfo().boundingBox.centerWorld;
            return {
                objects: active.filter(mesh => mesh.name.startsWith('collect-preview:')).map(mesh => mesh.parent.name),
                ground: center ? { x: center.x, y: center.y, z: center.z } : null
            };
        }
        """);

    private static Task BeginGestureProbeAsync(IPage page) => RenderAsync<JsonElement>(page, """
        () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const arm = scene.getTransformNodeByName('player-shoulder-right');
            globalThis.gatherArmMin = Infinity;
            globalThis.gatherObserver = scene.onAfterRenderObservable.add(() => {
                globalThis.gatherArmMin = Math.min(globalThis.gatherArmMin, arm.rotation.x);
            });
        }
        """);

    private static Task LookIntoExcavationAsync(IPage page) => RenderAsync<JsonElement>(page, """
        async () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            scene.activeCamera.beta = 0.3;
            await new Promise(resolve => scene.onAfterRenderObservable.addOnce(() => resolve()));
        }
        """);

    private static Task<CollectRecord[]> RecordsAsync(IPage page) => ReadAsync<CollectRecord[]>(page, """
        () => new Promise((resolve, reject) => {
            const db = indexedDB.open('ultima-backpack', 1);
            db.onerror = () => reject(db.error);
            db.onsuccess = () => {
                const request = db.result.transaction('collected').objectStore('collected').getAll();
                request.onsuccess = () => { db.result.close(); resolve(request.result); };
                request.onerror = () => { db.result.close(); reject(request.error); };
            };
        })
        """);

    private static async Task WaitForBridgeAsync(IPage page)
    {
        // A leitura inicial do Blazor comprova que a ponte da segunda aba está
        // ligada. Esperar toda a cena duplicaria a carga antes da concorrência.
        await page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                const read = window.game.backpack.read;
                window.game.backpack.read = function (...args) {
                    window.__bridgeBound = true;
                    return read.apply(this, args);
                };
            }, { once: true });
            """);
        await NavigateAsync(page);
        await UntilAsync(() => page.EvaluateAsync<bool>("() => window.__bridgeBound === true"),
            "a ponte da mochila não ligou na segunda aba", 300_000);
    }
}
