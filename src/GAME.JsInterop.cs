using Microsoft.JSInterop;

namespace Ultima;

/// <summary>
/// Ponte JS→C#: o loop de jogo em JavaScript (Babylon.js) chama estes métodos
/// para notificar a UI em Blazor. Cada método repassa para um callback da página.
/// </summary>
public sealed class GameInterop
{
    private readonly Action<string> _onEngineReady;
    private readonly Action<bool> _onNearChanged;
    private readonly Action _onInteract;
    private readonly Action _onClose;
    private readonly Action<double, double, double> _onMoveTick;

    public GameInterop(
        Action<string> onEngineReady,
        Action<bool> onNearChanged,
        Action onInteract,
        Action onClose,
        Action<double, double, double> onMoveTick)
    {
        _onEngineReady = onEngineReady;
        _onNearChanged = onNearChanged;
        _onInteract = onInteract;
        _onClose = onClose;
        _onMoveTick = onMoveTick;
    }

    [JSInvokable]
    public void OnEngineReady(string renderer) => _onEngineReady(renderer);

    [JSInvokable]
    public void OnNearChanged(bool near) => _onNearChanged(near);

    [JSInvokable]
    public void OnInteract() => _onInteract();

    [JSInvokable]
    public void OnClose() => _onClose();

    [JSInvokable]
    public void OnMoveTick(double x, double z, double fps) => _onMoveTick(x, z, fps);
}
