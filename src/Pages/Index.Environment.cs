using Microsoft.AspNetCore.Components;
using Ultima.Environments;

namespace Ultima.Pages;

public partial class Index
{
    [SupplyParameterFromQuery(Name = "world")] private string? TerrainEnvironmentId { get; set; }

    private void OpenEnvironments() =>
        Navigation.NavigateTo("environments" + new Uri(Navigation.Uri).Query, forceLoad: true);

    private async Task<EnvironmentDefinition> LoadTerrainAsync()
    {
        var catalog = await EnvironmentCatalog.LoadAsync(Http, _browserLifetime.Token);
        var requested = TerrainEnvironmentId ?? catalog.DefaultEnvironment;
        return catalog.Environments.FirstOrDefault(environment => environment.Id == requested && environment.Terrain is not null)
            ?? throw new InvalidDataException("Terrain environment unavailable.");
    }
}
