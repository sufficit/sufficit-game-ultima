using Microsoft.AspNetCore.Components;
using Ultima.Environments;
using Ultima.Models.Core;

namespace Ultima.Pages;

public partial class Index
{
    [SupplyParameterFromQuery(Name = "world")] private string? TerrainEnvironmentId { get; set; }

    private void OpenEnvironments() =>
        Navigation.NavigateTo("environments" + new Uri(Navigation.Uri).Query, forceLoad: true);

    private async Task<(EnvironmentDefinition Environment, HumanoidProfile? Profile)> LoadTerrainAsync()
    {
        var catalog = await EnvironmentCatalog.LoadAsync(Http, _browserLifetime.Token);
        var requested = TerrainEnvironmentId ?? catalog.DefaultEnvironment;
        var environment = catalog.Environments.FirstOrDefault(environment => environment.Id == requested && environment.Terrain is not null)
            ?? throw new InvalidDataException("Terrain environment unavailable.");
        return (environment, catalog.PlayerBody);
    }
}
