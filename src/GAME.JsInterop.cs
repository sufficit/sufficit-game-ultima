using Microsoft.JSInterop;

namespace Ultima;

/// <summary>
/// Ponte JS→C#: o loop de jogo, a rede P2P e a voz (JavaScript) chamam estes métodos
/// para notificar a UI em Blazor. Cada método repassa para um callback da página.
/// </summary>
public sealed class GameInterop
{
    public Action<string>? EngineReady { get; init; }
    public Action<bool>? NearChanged { get; init; }
    public Action<bool>? WandererNearChanged { get; init; }
    public Action? Interact { get; init; }
    public Action? Close { get; init; }
    public Action<double, double, double, double>? MoveTick { get; init; }
    public Action<string, int>? NetStatus { get; init; }
    public Action<string, string>? Chat { get; init; }
    public Action<int>? RemoteCount { get; init; }
    public Action<string>? MicState { get; init; }
    public Action<double>? MicLevel { get; init; }
    public Action<string>? SpeechTranscript { get; init; }
    public Action<string>? SpeechError { get; init; }
    public Action<bool>? Dictation { get; init; }
    public Action<string, double, string>? AiState { get; init; }
    public Action<string, double, string>? AiCpuState { get; init; }
    public Action<string>? WorkshopStatus { get; init; }

    [JSInvokable] public void OnEngineReady(string renderer) => EngineReady?.Invoke(renderer);
    [JSInvokable] public void OnNearChanged(bool near) => NearChanged?.Invoke(near);
    [JSInvokable] public void OnWandererNear(bool near) => WandererNearChanged?.Invoke(near);
    [JSInvokable] public void OnInteract() => Interact?.Invoke();
    [JSInvokable] public void OnClose() => Close?.Invoke();
    [JSInvokable] public void OnMoveTick(double x, double y, double z, double fps) => MoveTick?.Invoke(x, y, z, fps);
    [JSInvokable] public void OnNetStatus(string state, int peers) => NetStatus?.Invoke(state, peers);
    [JSInvokable] public void OnChat(string from, string text) => Chat?.Invoke(from, text);
    [JSInvokable] public void OnRemoteCount(int count) => RemoteCount?.Invoke(count);
    [JSInvokable] public void OnMicState(string state) => MicState?.Invoke(state);
    [JSInvokable] public void OnMicLevel(double level) => MicLevel?.Invoke(level);
    [JSInvokable] public void OnSpeechTranscript(string text) => SpeechTranscript?.Invoke(text);
    [JSInvokable] public void OnSpeechError(string message) => SpeechError?.Invoke(message);
    [JSInvokable] public void OnDictation(bool listening) => Dictation?.Invoke(listening);
    [JSInvokable] public void OnAiState(string state, double progress, string detail) => AiState?.Invoke(state, progress, detail);
    [JSInvokable] public void OnAiCpuState(string state, double progress, string detail) => AiCpuState?.Invoke(state, progress, detail);
    [JSInvokable] public void OnWorkshopStatus(string message) => WorkshopStatus?.Invoke(message);
}
