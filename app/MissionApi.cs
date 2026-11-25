using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ultima.Missions;

namespace Ultima.Server;

internal sealed class MissionApi(MissionStore store, string? authorKey, string? agentKey)
{
    internal const string Prefix = "/v1/game/environments";

    internal async Task HandleAsync(HttpContext context, string path)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
        response.Headers.AccessControlAllowMethods = "GET, HEAD, POST, OPTIONS";
        response.Headers.XContentTypeOptions = "nosniff";
        if (HttpMethods.IsOptions(request.Method))
        {
            response.StatusCode = 204;
            return;
        }

        var configuredAuthor = Configured(authorKey);
        var configuredAgent = Configured(agentKey);
        if (!configuredAuthor && !configuredAgent)
        {
            await Error(503, "missions_unavailable");
            return;
        }

        var header = request.Headers.Authorization.ToString();
        var provided = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : "";
        var author = configuredAuthor && Matches(provided, authorKey!);
        var agent = configuredAgent && Matches(provided, agentKey!);
        if (!author && !agent)
        {
            response.Headers.WWWAuthenticate = "Bearer";
            await Error(401, "mission_credentials_required");
            return;
        }

        var segments = path.Split('/');
        if (segments.Length < 6 || !MissionStore.IsId(segments[4]) || segments[5] != "missions")
        {
            await Error(404, "not_found");
            return;
        }

        var environment = segments[4];
        if (segments.Length == 6 && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            if (HttpMethods.IsHead(request.Method)) response.StatusCode = 200;
            else await response.WriteAsJsonAsync(store.Read(environment), MissionStore.Json, context.RequestAborted);
            return;
        }

        if (segments.Length == 6 && HttpMethods.IsPost(request.Method))
        {
            if (!author) { await Error(403, "mission_author_required"); return; }
            await Result(store.Create(environment, await Read<MissionDefinition>()));
            return;
        }
        if (segments is ["", "v1", "game", "environments", _, "missions", var id, "events"] && HttpMethods.IsPost(request.Method))
        {
            await Result(store.Record(environment, id, await Read<MissionEvent>()));
            return;
        }
        await Error(404, "not_found");

        async Task<T?> Read<T>() where T : class
        {
            if (request.ContentLength is not (>= 1 and <= 16384) || !request.HasJsonContentType()) return null;
            try { return await request.ReadFromJsonAsync<T>(MissionStore.Json, context.RequestAborted); }
            catch (JsonException) { return null; }
        }

        async Task Result((int Status, string Error, MissionView? Value) result)
        {
            if (result.Value is null) { await Error(result.Status, result.Error); return; }
            response.StatusCode = result.Status;
            await response.WriteAsJsonAsync(result.Value, MissionStore.Json, context.RequestAborted);
        }

        Task Error(int status, string code)
        {
            response.StatusCode = status;
            return response.WriteAsJsonAsync(new { error = code }, MissionStore.Json, context.RequestAborted);
        }
    }

    private static bool Configured(string? value) => value is { Length: >= 32 and <= 512 } &&
        value.All(character => character is > ' ' and < '\u007f');

    private static bool Matches(string provided, string expected) => provided.Length <= 512 &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
