using Microsoft.JSInterop;
using System.Text.Json;
using Ultima.Browser;

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
    public Action<string?>? ObjectNear { get; init; }
    public Func<CollectedResource, GroundPoint?, Task>? CollectResource { get; init; }
    public Func<Task>? BackpackRefresh { get; init; }
    public Func<JsonElement, Task>? WorldCommand { get; init; }
    public Func<string, string, string, string, long, Task>? DialogTurn { get; init; }
    public Func<bool, bool, Task<bool>>? DialogFlush { get; init; }
    public Func<DialogLogState>? DialogDebug { get; init; }
    public Func<double, double, double, Task>? LivePose { get; init; }
    public Func<string, Task>? LiveChat { get; init; }
    public Action<string>? BackpackStatus { get; init; }
    public Action<string, string, JsonElement>? AnimationMarker { get; init; }
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

    [JSInvokable] public void OnEngineReady(string renderer) => EngineReady?.Invoke(renderer);
    [JSInvokable] public void OnNearChanged(bool near) => NearChanged?.Invoke(near);
    [JSInvokable] public void OnWandererNear(bool near) => WandererNearChanged?.Invoke(near);
    [JSInvokable] public void OnObjectNear(string? label) => ObjectNear?.Invoke(label);
    [JSInvokable] public Task OnCollectResource(CollectedResource resource, GroundPoint? point) => CollectResource?.Invoke(resource, point) ?? Task.CompletedTask;
    [JSInvokable] public Task OnBackpackRefresh() => BackpackRefresh?.Invoke() ?? Task.CompletedTask;
    [JSInvokable] public Task OnWorldCommand(JsonElement operation) => WorldCommand?.Invoke(operation) ?? Task.CompletedTask;
    [JSInvokable] public Task OnDialogTurn(string npc, string player, string reply, string source, long ms)
        => DialogTurn?.Invoke(npc, player, reply, source, ms) ?? Task.CompletedTask;
    [JSInvokable] public Task<bool> OnDialogFlush(bool force, bool keepalive)
        => DialogFlush?.Invoke(force, keepalive) ?? Task.FromResult(false);
    [JSInvokable] public DialogLogState? OnDialogDebug() => DialogDebug?.Invoke();
    [JSInvokable] public Task OnLivePose(double x, double z, double yaw) => LivePose?.Invoke(x, z, yaw) ?? Task.CompletedTask;
    [JSInvokable] public Task OnLiveChat(string text) => LiveChat?.Invoke(text) ?? Task.CompletedTask;
    [JSInvokable] public void OnBackpackStatus(string message) => BackpackStatus?.Invoke(message);
    [JSInvokable] public void OnAnimationMarker(string name, string payload, JsonElement target)
        => AnimationMarker?.Invoke(name, payload, target);
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
}
