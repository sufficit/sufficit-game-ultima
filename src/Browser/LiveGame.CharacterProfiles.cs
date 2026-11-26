using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;
using Ultima.Models.Core;

namespace Ultima.Browser;

public sealed partial class LiveGame
{
    private const string BodyRoute = "/v1/game/characters/me/body";

    // Read before starting the scene so model, rig, camera, and physics share one body.
    public async Task<HumanoidProfile?> LoadCharacterBodyAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var context = await js.InvokeAsync<Context>("game.live.context", timeout.Token);
            if (!context.Enabled || string.IsNullOrEmpty(context.ProfileKey)) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, context.Api + BodyRoute);
            request.Headers.Add("X-Character-Profile-Key", context.ProfileKey);
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync(CharacterBodyJson.Default.CharacterBodyResponse, timeout.Token);
            if (result?.Profile is not { } profile) return null;
            _ = Humanoid.ResolveBody(profile);
            return profile;
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested &&
            error is HttpRequestException or JsonException or OperationCanceledException or JSException or ArgumentException)
        {
            return null;
        }
    }
}

internal sealed record CharacterBodyResponse(HumanoidProfile? Profile);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CharacterBodyResponse))]
internal sealed partial class CharacterBodyJson : JsonSerializerContext;
