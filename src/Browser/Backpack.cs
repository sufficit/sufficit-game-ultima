using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed record GroundPoint(double X, double Z);
public sealed record TerrainBrush(double X, double Z, double R);

public sealed record CollectedResource(string? Id, string Item, string Kind, double? X, double? Z, TerrainBrush? Brush);

public sealed class Backpack(IJSRuntime js)
{
    private readonly SemaphoreSlim _refresh = new(1);
    private int _appliedCount = -1;

    public event Action<Dictionary<string, int>>? Changed;
    public event Action<string>? Status;

    public async Task SynchronizeAsync()
    {
        await _refresh.WaitAsync();
        try
        {
            var entries = await js.InvokeAsync<CollectedResource[]>("game.backpack.read");
            if (entries.Length <= _appliedCount) return;
            await js.InvokeVoidAsync("game.setCollectedResources", (object)entries);
            _appliedCount = entries.Length;
            Changed?.Invoke(entries.GroupBy(entry => entry.Item, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        }
        catch (JSException)
        {
            Status?.Invoke("A mochila não conseguiu acessar o armazenamento deste navegador.");
        }
        finally { _refresh.Release(); }
    }

    public async Task CollectAsync(CollectedResource resource, GroundPoint? point)
    {
        var soil = resource.Kind == "soil";
        var entry = soil && string.IsNullOrEmpty(resource.Id)
            ? resource with { Id = $"dig:{Guid.NewGuid():N}" }
            : resource;
        string result;
        try
        {
            result = await js.InvokeAsync<string>("game.backpack.add", entry);
        }
        catch (JSException)
        {
            Status?.Invoke("A coleta não pôde ser salva. O recurso continua no cenário.");
            return;
        }

        await SynchronizeAsync();
        Status?.Invoke(result == "added" ? $"+1 {resource.Item}" : "Esse recurso já foi coletado.");
    }
}
