internal sealed partial class WorldCollision
{
    private async Task RemoteAsync()
    {
        await page.EvaluateAsync("() => game.teleportToGuardian()");
        await Task.Delay(500);
        await page.Keyboard.PressAsync("e");
        await page.Locator(".ultima-dialog").WaitForAsync();
        await WaitAsync(async () =>
        {
            var pose = await Probe<ConversationPose>("conversation");
            return pose.Mouth > .016 && pose.Listening > .06 && Math.Cos(pose.NpcYaw) > .9 && Math.Cos(pose.PlayerYaw) < -.9;
        }, "os personagens se encaram, a Guardiã fala e o jogador escuta");
        await page.Keyboard.PressAsync("Escape");

        const string id = "gesture-test";
        await page.EvaluateAsync("""
            id => {
                game.addRemote(id, 'Visitante');
                game.updateRemote(id, 1, 8, 0);
                game.updateRemote(id, 1, 10, 0);
            }
            """, id);
        await WaitAsync(async () =>
        {
            var pose = await Probe<RemotePose>("remotePose");
            return pose.Actor?.Z is > 8 and < 9.999 && Math.Abs(pose.Arm) > .03;
        }, "o remoto interpola a posição e move os braços");
        Task<bool> HasShadowAsync() => Probe<bool>("remoteShadow");
        await WaitAsync(HasShadowAsync, "avatar próximo projeta sombra");

        await MoveRemoteAsync(id, 40, 8);
        await WaitAsync(async () =>
        {
            var pose = await Probe<RemotePose>("remotePose");
            return pose.Actor?.X == 40 && pose.Label?.X == 40 &&
                Math.Abs(pose.Label.Y - pose.Actor.Y - 1.925) < 1e-6;
        }, "o teleporte distante mantém o rótulo junto ao avatar");
        await WaitAsync(async () => !await HasShadowAsync(), "avatar distante sai do mapa de sombras");

        var camera = await Probe<CameraPose>("camera");
        await MoveRemoteAsync(id, camera.Position.X - camera.Forward.X * 200, camera.Position.Z - camera.Forward.Z * 200);
        async Task<bool> EnabledAsync(bool expected)
        {
            var pose = await Probe<RemotePose>("remotePose");
            return pose.ActorEnabled == expected && pose.LabelEnabled == expected;
        }
        await WaitAsync(() => EnabledAsync(false), "avatar e rótulo desativados atrás da câmera");

        await MoveRemoteAsync(id, 1, 8);
        await WaitAsync(() => EnabledAsync(true), "avatar e rótulo voltam ao se aproximar");
        await WaitAsync(HasShadowAsync, "avatar que retorna volta a projetar sombra");
        await RemoveRemoteAsync(id);
        await WaitAsync(async () => await Probe<RemotePose>("remotePose") is { Actor: null, Label: null },
            "a desconexão remove o avatar e seu rótulo");
        Check(!await HasShadowAsync(), "desconexão remove a sombra");
        Log(new { conversation = true, remoteMotion = true });
    }

    private sealed record ConversationPose(double Mouth, double Listening, double NpcYaw, double PlayerYaw);
    private sealed record RemotePose(Position? Actor, Position? Label, bool? ActorEnabled, bool? LabelEnabled, double Arm);
    private sealed record CameraPose(Position Position, Position Forward);
}
