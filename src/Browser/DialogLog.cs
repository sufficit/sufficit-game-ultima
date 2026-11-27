using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace Ultima.Browser;

public sealed record DialogTurn(string Id, long T, string Npc, string Player, string Reply, string Source, string Model, int Ms, string? PlayerUuid = null);
public sealed record DialogLogState(bool Enabled, string Api, int Queued, bool Sending, int IntervalMs, long RetryInMs,
    int SentTotal, long LastSentAt, string LastError);

public sealed class DialogLog(IJSRuntime js)
{
    private const string QueueKey = "ultima-dialog-queue";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _store = new(1);
    private List<DialogTurn> _queue = [];
    private string _url = "";
    private string _build = "dev";
    private bool _enabled;
    private bool _sending;
    private int _interval = 180000;
    private int _batchSize = 200;
    private int _maxQueue = 500;
    private int _failures;
    private int _sentTotal;
    private long _lastSentAt;
    private long _retryAt;
    private string _lastError = "";

    public event Action<int>? Changed;

    public async Task InitializeAsync(string build, CancellationToken token)
    {
        _build = build.StartsWith("__", StringComparison.Ordinal) ? "dev" : Limit(build, 20);
        var context = await js.InvokeAsync<Context>("game.dialogLog.context", token);
        _url = context.Api.TrimEnd('/') + (context.Endpoint ?? "/v1/analytics/dialogs");
        _enabled = context.Enabled;
        _interval = Math.Max(15000, context.IntervalMs ?? _interval);
        _batchSize = Math.Clamp(context.BatchSize ?? _batchSize, 1, 200);
        _maxQueue = Math.Clamp(context.MaxQueue ?? _maxQueue, 10, 2000);
        _queue = await LoadAsync<List<DialogTurn>>(QueueKey) ?? [];
        Changed?.Invoke(_queue.Count);
    }

    public async Task WatchAsync(CancellationToken token)
    {
        if (!_enabled) return;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_interval));
            while (await timer.WaitForNextTickAsync(token)) await FlushAsync(false, false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async Task RecordAsync(string npc, string player, string reply, string source, long ms)
    {
        npc = Clean(npc, 40);
        player = Clean(player, 500);
        reply = Clean(reply, 500);
        source = Clean(source, 60);
        if (npc.Length == 0 || (player.Length == 0 && reply.Length == 0)) return;
        var engine = source.StartsWith("GPU do jogador", StringComparison.Ordinal) ? "game.ai.status"
            : source.StartsWith("CPU do jogador", StringComparison.Ordinal) ? "game.aiCpu.status" : null;
        var model = engine is null ? "" : Clean((await js.InvokeAsync<ModelInfo?>(engine))?.Model, 60);

        var playerUuid = await js.InvokeAsync<string>("game.player.uuid");
        await _store.WaitAsync();
        try
        {
            var now = Now();
            if (_queue.LastOrDefault() is { } last && now - last.T < 2000
                && last.Npc == npc && last.Player == player && last.Reply == reply) return;

            var turn = new DialogTurn($"d{Guid.NewGuid():N}", now, npc, player, reply, source,
                model, (int)Math.Clamp(ms, 0, 600000), playerUuid);
            _queue.Add(turn);
            Trim(_queue, _maxQueue);
            await SaveQueueAsync();
        }
        finally { _store.Release(); }
    }

    public async Task<bool> FlushAsync(bool force, bool keepalive)
    {
        if (!_enabled || _sending || _queue.Count == 0 || (!force && Now() < _retryAt)) return false;
        _sending = true;
        DialogBatch? batch = null;
        try
        {
            batch = new DialogBatch(_build, _queue.Take(_batchSize).ToArray());
            var ack = await js.InvokeAsync<Receipt>("game.dialogLog.send", _url, batch, keepalive);
            if (!ack.Ok || ack.Ids is null) throw new InvalidDataException("bad_payload");

            await _store.WaitAsync();
            try
            {
                var confirmed = ack.Ids.ToHashSet(StringComparer.Ordinal);
                _sentTotal += _queue.RemoveAll(turn => confirmed.Contains(turn.Id));
                await SaveQueueAsync();
            }
            finally { _store.Release(); }
            _lastSentAt = Now();
            _failures = 0;
            _retryAt = 0;
            _lastError = "";
            return true;
        }
        catch (Exception ex) when (ex is JSException or InvalidDataException)
        {
            _lastError = ex.Message;
            if (force && batch is not null && await js.InvokeAsync<bool>("game.dialogLog.beacon", _url, batch))
            {
                _lastError = "beacon_sem_confirmacao";
                return true;
            }
            _retryAt = Now() + Math.Min(30000L << Math.Min(_failures++, 5), 600000);
            return false;
        }
        finally { _sending = false; }
    }

    public DialogLogState Debug() => new(_enabled, _url, _queue.Count, _sending, _interval,
        Math.Max(0, _retryAt - Now()), _sentTotal, _lastSentAt, _lastError);

    private async Task<T?> LoadAsync<T>(string key)
    {
        var raw = await js.InvokeAsync<string?>("game.browser.read", key);
        try { return raw is null ? default : JsonSerializer.Deserialize<T>(raw, Json); }
        catch (JsonException) { return default; }
    }

    private async Task SaveQueueAsync()
    {
        await SaveAsync(QueueKey, _queue);
        Changed?.Invoke(_queue.Count);
    }

    private ValueTask SaveAsync<T>(string key, T value)
        => js.InvokeVoidAsync("game.browser.write", key, JsonSerializer.Serialize(value, Json));

    private static void Trim<T>(List<T> list, int limit)
    {
        if (list.Count > limit) list.RemoveRange(0, list.Count - limit);
    }
    private static string Clean(string? value, int limit)
        => Limit(Regex.Replace(Regex.Replace(value ?? "", "[\\x00-\\x1f\\x7f<>]", ""), @"\s+", " ").Trim(), limit);
    private static string Limit(string value, int limit) => value[..Math.Min(value.Length, limit)];
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed record Context(string Api, string? Endpoint, bool Enabled, int? IntervalMs, int? BatchSize, int? MaxQueue);
    private sealed record DialogBatch(string Build, DialogTurn[] Turns);
    private sealed record Receipt(bool Ok, string[]? Ids);
    private sealed record ModelInfo(string? Model);
}
