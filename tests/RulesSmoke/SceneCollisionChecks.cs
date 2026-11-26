using Ultima.Terrain;

internal static class SceneCollisionChecks
{
    internal static void Run(Action<string, bool> check)
    {
        var movement = new PlayerMovement(1.75, .41, .03);
        movement.SetSceneBounds([-10, 10, -10, 10]);
        movement.SetSolidBoxes([-.05, .05, 0, 3, -2, 2, -.05, .05, 0, .2, 3, 5]);

        var wall = movement.Sweep(-1, .03, 0, 4, 0, 0);
        check("The shared sweep stops at a thin wall", wall.X is > -.302 and < -.30);
        var tangent = movement.Sweep(-1, .03, 0, 3, 1, 0);
        check("The shared sweep slides along the wall", tangent.X < -.30 && tangent.Z > .99);
        var doorway = movement.Sweep(-1, .03, 2.5, 4, 0, 0);
        check("The shared sweep passes through an opening", doorway.X == 3);
        var step = movement.Sweep(-1, .03, 4, 4, 0, 0);
        check("The shared sweep lets the player step onto a low solid", step.X == 3);
        var edge = movement.Sweep(8, .03, 8, 4, 0, 0);
        check("Scene bounds include character clearance", edge.X == 9.75);
        var surface = new TerrainSurface(flatHeight: 0);
        check("The authored floor supplies the shared foot and movement height", surface.Height(0, 5) == 0);
    }
}
