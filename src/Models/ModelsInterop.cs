using Microsoft.JSInterop;

namespace Ultima.Models;

/// <summary>
/// Ponte JS→C# para a variação paramétrica de cenário (PLAN-PLAYER-ASSETS, fase 2):
/// o cliente chama <c>invokeMethodAsync('VaryModel', id, seed, detail)</c> e recebe o
/// descritor JSON reconstruído com os novos parâmetros, trocando as malhas em cena.
/// </summary>
public sealed class ModelsInterop(ModelLoader loader)
{
    [JSInvokable]
    public string? VaryModel(string id, int seed, double detail) => loader.Rebuild(id, seed, detail);
}
