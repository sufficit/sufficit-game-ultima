using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Ultima.Models.Core;

namespace Ultima.Server;

/// <summary>Roteador HTTP: mesmas rotas, códigos e cabeçalhos do backend anterior (main.py).</summary>
sealed class Api(ContentStore content, GameWorld game, Signaling net, NpcChat chat, ILogger log)
{
    const int MaxBody = 64 * 1024;
    const string ApiVersion = "v1";
    const string DefaultCache = "public, max-age=60, stale-while-revalidate=300";
    const string NoStore = "no-store";

    static readonly string[] Endpoints =
    [
        "/v1/worlds/ultima", "/v1/npcs/guardia", "POST /v1/npcs/guardia/chat",
        "/v1/quests", "/v1/announcements",
        "/v1/net/rooms", "/v1/net/{room}/join", "/v1/net/{room}/signal", "/v1/net/{room}/poll/{peer}",
        "POST /v1/game/sessions", "/v1/game/sessions", "/v1/game/state",
        "POST /v1/game/command", "/v1/game/events", "/v1/game/map", "POST /v1/game/map/edits",
        "POST /v1/game/leave",
    ];

    public async Task HandleAsync(HttpContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var path = req.Path.Value ?? "/";
            path = path.TrimEnd('/');
            if (path.Length == 0) path = "/";
            switch (req.Method)
            {
                case "OPTIONS": await Send(ctx, 204, null, DefaultCache); break;
                case "GET": case "HEAD": await GetAsync(ctx, path); break;
                case "POST": await PostAsync(ctx, path); break;
                case "DELETE": await DeleteAsync(ctx, path); break;
                default: await Json(ctx, J.Err("method_not_allowed"), 405); break;
            }
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            log.LogError("request error: {Type}", ex.GetType().Name);
            await Json(ctx, J.Err("internal_error"), 500, NoStore);
        }
    }

    // ---------- resposta ----------
    static async Task Send(HttpContext ctx, int status, byte[]? body, string cache, string? etag = null)
    {
        var res = ctx.Response;
        res.StatusCode = status;
        var h = res.Headers;
        h.ContentType = "application/json; charset=utf-8";
        h.AccessControlAllowOrigin = "*";
        h.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
        h.AccessControlAllowHeaders = "Content-Type, If-None-Match, X-Game-Token";
        h.AccessControlExposeHeaders = "ETag, Cache-Control";
        h.CacheControl = cache;
        h.XContentTypeOptions = "nosniff";
        if (etag is not null) h.ETag = etag;
        res.ContentLength = body?.Length ?? 0;
        if (body is { Length: > 0 } && ctx.Request.Method != "HEAD")
            await res.Body.WriteAsync(body);
    }

    /// <summary>cache=null: conteúdo público com ETag (304 quando If-None-Match bate).</summary>
    static Task Json(HttpContext ctx, JsonNode payload, int status = 200, string? cache = null)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString(J.Opts));
        if (cache is not null) return Send(ctx, status, body, cache);
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant() + "\"";
        if (ctx.Request.Headers.IfNoneMatch.ToString() == etag)
            return Send(ctx, 304, null, DefaultCache, etag);
        return Send(ctx, status, body, DefaultCache, etag);
    }

    static Task Json(HttpContext ctx, (int Status, JsonObject Body) r) => Json(ctx, r.Body, r.Status, NoStore);

    static async Task<JsonObject?> Body(HttpContext ctx)
    {
        var len = ctx.Request.ContentLength ?? 0;
        if (len <= 0 || len > MaxBody) return null;
        var buf = new byte[len];
        var read = 0;
        while (read < len)
        {
            var n = await ctx.Request.Body.ReadAsync(buf.AsMemory(read));
            if (n == 0) break;
            read += n;
        }
        try { return JsonNode.Parse(buf.AsSpan(0, read)) as JsonObject; }
        catch (Exception) { return null; }
    }

    static string? Token(HttpContext ctx, JsonObject? body = null)
    {
        var h = ctx.Request.Headers["X-Game-Token"].ToString();
        if (h.Length > 0) return h;
        if (body is not null) return J.StrOrNull(body["token"]);
        var q = ctx.Request.Query["token"].ToString();
        return q.Length > 0 ? q : null;
    }

    static string Ip(HttpContext ctx)
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        return J.Cut(fwd.Length > 0 ? fwd : ctx.Connection.RemoteIpAddress?.ToString() ?? "", 45);
    }

    static double QueryFloat(HttpContext ctx, string name, double dflt) =>
        double.TryParse(ctx.Request.Query[name].ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : dflt;

    // ---------- GET ----------
    async Task GetAsync(HttpContext ctx, string path)
    {
        switch (path)
        {
            case "/health":
                await Json(ctx, new JsonObject
                {
                    ["status"] = "ok", ["apiVersion"] = ApiVersion, ["uptime"] = game.Uptime,
                    ["runtime"] = "aspnet", ["modelsCoreApi"] = ModelCore.ApiVersion,
                });
                return;
            case "/v1/net/rooms": await Json(ctx, new JsonObject { ["rooms"] = net.Rooms() }, 200, NoStore); return;
            case "/v1/game/sessions": await Json(ctx, game.Sessions(), 200, NoStore); return;
            case "/v1/game/state": await Json(ctx, game.State(Token(ctx))); return;
            case "/v1/game/map": await Json(ctx, game.Map(), 200, NoStore); return;
            case "/v1/game/events":
            {
                var since = long.TryParse(ctx.Request.Query["since"].ToString(), out var s) ? s : 0;
                await Json(ctx, await game.EventsAsync(Token(ctx), since, QueryFloat(ctx, "wait", 15)));
                return;
            }
        }
        var parts = path.Split('/');
        if (parts.Length == 6 && parts[1] == "v1" && parts[2] == "net" && parts[4] == "poll")
        {
            await Json(ctx, await net.PollAsync(J.Cut(parts[3], 40), J.Cut(parts[5], 40), QueryFloat(ctx, "wait", 15)), 200, NoStore);
            return;
        }

        var c = content.Load();
        if (c is null) { await Json(ctx, J.Err("content_unavailable"), 503); return; }
        JsonObject With(string id, JsonNode? src)
        {
            var o = new JsonObject { ["id"] = id };
            if (src is JsonObject so) foreach (var (k, v) in so) o[k] = v?.DeepClone();
            return o;
        }
        switch (path)
        {
            case "/v1/manifest":
                await Json(ctx, new JsonObject
                {
                    ["apiVersion"] = ApiVersion, ["schema"] = c["$schema"]?.DeepClone(),
                    ["contentVersion"] = c["version"]?.DeepClone(), ["endpoints"] = J.StrArr(Endpoints),
                });
                break;
            case "/v1/worlds/ultima": await Json(ctx, With("ultima", c["world"])); break;
            case "/v1/npcs/guardia":
                if ((c["npcs"] as JsonObject)?["guardia"] is JsonObject npc) await Json(ctx, With("guardia", npc));
                else await Json(ctx, J.Err("not_found"), 404);
                break;
            case "/v1/quests": await Json(ctx, new JsonObject { ["items"] = c["quests"]?.DeepClone() ?? new JsonArray() }); break;
            case "/v1/announcements": await Json(ctx, new JsonObject { ["items"] = c["announcements"]?.DeepClone() ?? new JsonArray() }); break;
            default: await Json(ctx, J.Err("not_found"), 404); break;
        }
    }

    // ---------- POST ----------
    async Task PostAsync(HttpContext ctx, string path)
    {
        if (path is "/v1/game/map/edits" or "/v1/game/sessions" or "/v1/game/command" or "/v1/game/leave" or "/v1/npcs/guardia/chat")
        {
            var data = await Body(ctx);
            if (data is null) { await Json(ctx, J.Err("bad_request"), 400, NoStore); return; }
            switch (path)
            {
                case "/v1/game/map/edits": await Json(ctx, game.MapEdit(Token(ctx, data), data)); return;
                case "/v1/game/sessions": await Json(ctx, game.NewSession(J.Str(data["name"]))); return;
                case "/v1/game/command": await Json(ctx, await game.CommandAsync(Token(ctx, data), data, Ip(ctx))); return;
                case "/v1/game/leave": await Json(ctx, game.Leave(Token(ctx, data))); return;
            }
            var text = J.Clean(data["message"], NpcChat.MaxMsg);
            var session = J.Clean(data["session"], 40);
            if (text.Length == 0 || session.Length == 0)
            {
                await Json(ctx, J.Err("message_and_session_required"), 400, NoStore);
                return;
            }
            var c = content.Load();
            if (c is null) { await Json(ctx, J.Err("content_unavailable"), 503, NoStore); return; }
            await Json(ctx, await chat.ChatAsync(content.Guardia(), session, Ip(ctx), J.Clean(data["player"], 40), text));
            return;
        }

        var parts = path.Split('/');
        if (parts.Length == 5 && parts[1] == "v1" && parts[2] == "net")
        {
            var room = J.Cut(parts[3], 40);
            var action = parts[4];
            var data = await Body(ctx);
            if (data is null) { await Json(ctx, J.Err("bad_request"), 400, NoStore); return; }
            var peer = J.Str(data["peer"]);
            if (peer.Length == 0) peer = J.Str(data["from"]);
            peer = J.Cut(peer, 40);
            if (peer.Length == 0) { await Json(ctx, J.Err("peer_required"), 400, NoStore); return; }
            switch (action)
            {
                case "join":
                {
                    var name = J.Str(data["name"]);
                    var peers = net.Join(room, peer, name.Length > 0 ? name : "Jogador");
                    await Json(ctx, new JsonObject { ["peer"] = peer, ["peers"] = J.StrArr(peers) }, 200, NoStore);
                    return;
                }
                case "leave":
                    net.Leave(room, peer);
                    await Json(ctx, new JsonObject { ["ok"] = true }, 200, NoStore);
                    return;
                case "signal":
                {
                    var kind = J.Str(data["kind"]);
                    if (kind is not ("offer" or "answer" or "ice" or "bye"))
                    {
                        await Json(ctx, J.Err("bad_kind"), 400, NoStore);
                        return;
                    }
                    var target = J.StrOrNull(data["to"]) is { } t ? J.Cut(t, 40) : null;
                    var sent = net.Signal(room, peer, target, kind, data["data"]);
                    await Json(ctx, new JsonObject { ["ok"] = true, ["delivered"] = sent }, 200, NoStore);
                    return;
                }
            }
        }
        await Json(ctx, J.Err("method_not_allowed"), 405);
    }

    async Task DeleteAsync(HttpContext ctx, string path)
    {
        if (path == "/v1/game/session") { await Json(ctx, game.Leave(Token(ctx))); return; }
        await Json(ctx, J.Err("method_not_allowed"), 405);
    }
}
