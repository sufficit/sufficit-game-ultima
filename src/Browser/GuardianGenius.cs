using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Sufficit.AI.Genius.Embedded;

namespace Ultima.Browser;

public sealed class GuardianGenius(IJSRuntime js) : IAsyncDisposable
{
    internal const string SourcePrefix = "Genius local";
    private Task<IJSObjectReference>? _module;

    public async Task<string> ReplyAsync(string prompt, string text, string engine, int seconds,
        int maxTokens, CancellationToken token, Func<string, string>? interpret = null, string? toolsJson = null)
    {
        var module = await ModuleAsync();
        var id = await module.InvokeAsync<string>("guardianConversationId", token);
        var runtime = new ConversationRuntime(new IndexedConversationStorage(module));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            return await runtime.SubmitAsync(id, prompt, text, (request, ct) =>
                module.InvokeAsync<string?>("generate", ct, requestId, engine,
                    JsonSerializer.Serialize(request, GuardianGeniusJson.Default.InferenceRequest), maxTokens, id, toolsJson),
                interpret is null ? null : (answer, _) => ValueTask.FromResult(interpret(answer)), timeout.Token);
        }
        finally
        {
            if (timeout.IsCancellationRequested)
                await module.InvokeVoidAsync("cancel", CancellationToken.None, requestId);
        }
    }

    public async Task<string?> MemoryCommandAsync(string text, CancellationToken token)
    {
        var trimmed = text.Trim();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end])) end++;
        var command = trimmed[..end].ToLowerInvariant();
        if (command == "/memórias") command = "/memorias";
        if (command is not ("/lembrar" or "/memorias")) return null;
        try
        {
            var module = await ModuleAsync();
            return await module.InvokeAsync<string>("handleMemoryCommand", token, command, trimmed[end..].Trim());
        }
        catch (JSException) { return "Não consegui acessar a memória neste navegador."; }
    }

    private async Task<IJSObjectReference> ModuleAsync()
    {
        var import = _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/guardian-genius.mjs").AsTask();
        try { return await import; }
        catch
        {
            if (ReferenceEquals(_module, import)) _module = null;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
            await (await _module).DisposeAsync();
    }

    private sealed class IndexedConversationStorage(IJSObjectReference module) : IConversationStorage
    {
        public async ValueTask<ConversationSnapshot?> LoadAsync(string id, CancellationToken cancellationToken)
        {
            var json = await module.InvokeAsync<string?>("load", cancellationToken, id);
            return json is null ? null : JsonSerializer.Deserialize(json, GuardianGeniusJson.Default.ConversationSnapshot);
        }

        public ValueTask<bool> TrySaveAsync(string id, ConversationSnapshot next, CancellationToken cancellationToken) =>
            module.InvokeAsync<bool>("save", cancellationToken, id, next.Revision,
                JsonSerializer.Serialize(next, GuardianGeniusJson.Default.ConversationSnapshot));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InferenceRequest))]
[JsonSerializable(typeof(ConversationSnapshot))]
internal sealed partial class GuardianGeniusJson : JsonSerializerContext;
