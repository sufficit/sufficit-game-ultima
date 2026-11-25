using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BrowserTest;

/// <summary>
/// Registro de conversas contra o backend real: a resposta da Guardiã entra na fila do navegador,
/// flush() envia o lote e recebe a confirmação por ids, o servidor lista o turno e o pagehide
/// envia o que ficou pendente. Substitui tests/dialog-log.e2e.js. Como world-undo, precisa de um
/// backend descartável em ULTIMA_API. O cenário não configura IA: sem modelo no servidor e no
/// navegador, a Guardiã responde com o aviso de indisponibilidade e o turno é registrado do mesmo jeito.
/// </summary>
internal static class DialogLogLive
{
    private const string QueueKey = "ultima-dialog-queue";
    private const string Question = "Onde fica o santuário?";
    private const string Unavailable = "Minha IA está indisponível";

    private static readonly Regex LocalBackend = new(@"^http://127\.0\.0\.1:\d+$", RegexOptions.CultureInvariant);

    internal static async Task RunAsync(IBrowser browser)
    {
        var site = Environment.GetEnvironmentVariable("ULTIMA_SITE") ?? "https://sufficit.github.io/sufficit-game-ultima/";
        var api = Environment.GetEnvironmentVariable("ULTIMA_API");
        Check(Uri.TryCreate(site, UriKind.Absolute, out var siteUrl) && LocalBackend.IsMatch(api ?? ""),
            "o cenário precisa de um backend descartável local em ULTIMA_API", new { site, api });
        // HTTPS não chama HTTP direto: só no ensaio, a origem do site encaminha ao servidor local.
        var proxy = siteUrl!.Scheme == "https" ? new Uri(siteUrl, "__dialog-test-api__/") : null;
        var apiBase = (proxy?.AbsoluteUri ?? api!).TrimEnd('/');
        var url = new UriBuilder(siteUrl)
        {
            Query = "rede=0&api=" + Uri.EscapeDataString(apiBase) + (MainThread ? "&render=main&gpu=0" : "")
        }.Uri.AbsoluteUri;

        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 720 }
        });
        var page = await context.NewPageAsync();
        var traffic = new Traffic(page, apiBase, proxy?.AbsolutePath.TrimEnd('/') ?? "").Start();
        var conflicts = new CommandConflicts(page, proxy?.AbsolutePath ?? "");
        try
        {
            if (proxy is not null)
                await page.RouteAsync(proxy.AbsoluteUri + "**", async route =>
                {
                    var request = new Uri(route.Request.Url);
                    var response = await route.FetchAsync(new()
                    {
                        Url = $"{api}/{request.AbsolutePath[proxy.AbsolutePath.Length..]}{request.Query}"
                    });
                    await route.FulfillAsync(new() { Response = response });
                });
            var cpu = await context.APIRequest.GetAsync(new Uri(siteUrl, "js/ai-cpu.js").AbsoluteUri);
            Check(cpu.Ok, "js/ai-cpu.js servido", cpu.Status);

            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
            var clock = Stopwatch.StartNew();
            var build = await CheckBuildAsync(page);
            await WaitSceneAsync(page, url, traffic);

            await WaitAsync(() => GuardAsync(() => page.EvaluateAsync<bool>("() => game.teleportToGuardian()")),
                "chega à Guardiã", 30_000, 1000);
            await page.Locator(".ultima-hint").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            try { await page.Locator("#game-canvas").FocusAsync(); }
            catch (PlaywrightException) { /* o atalho de teclado segue valendo no documento */ }
            var opened = false;
            for (var attempt = 0; attempt < 4 && !opened; attempt++)
            {
                await page.Keyboard.PressAsync("e");
                opened = await PollAsync(() => VisibleAsync(page, ".ultima-dialog"), 10_000, 500);
            }
            Check(opened, "diálogo abre com E junto à Guardiã", traffic.Errors());
            var greeting = "";
            await WaitAsync(async () => (greeting = await LineAsync(page)).Length > 3, "saudação da Guardiã", 15_000, 500);
            // O teleporte é local: a pose chega ao servidor no envio seguinte. Conversar antes disso faz
            // o servidor recusar por distância (guardian_out_of_reach) e a Guardiã só pedir aproximação.
            await WaitAsync(async () => Distance(await GuardAsync(() => ReadAsync<Position?>(page,
                    "async api => { const r = await fetch(api + '/v1/game/state', { headers: { 'X-Game-Token': game.live.token() } }); const s = await r.json(); return s.me ? { x: s.me.x, z: s.me.z } : null; }",
                    apiBase)), Guardian) <= 3, "pose do teleporte registrada no servidor", 20_000, 500);

            var input = page.Locator("#dialog-input");
            await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await input.FillAsync(Question);
            await input.PressAsync("Enter");
            var reply = "";
            await WaitAsync(async () => (reply = await LineAsync(page)).Length > 0 && reply != greeting && !reply.Contains('…'),
                "resposta da Guardiã", 45_000, 1000);
            var unavailable = await traffic.SeenAsync(503, "/v1/game/garden/dialog");
            Check(!unavailable || reply.StartsWith(Unavailable, StringComparison.Ordinal),
                "sem IA no servidor, a Guardiã avisa que está indisponível", new { reply, unavailable });

            DialogEntry[] queue = [];
            await WaitAsync(async () => (queue = await QueueAsync(page)).Length >= 1, "turno na fila", 10_000, 500);
            var turn = queue.Last();
            Check(turn.Npc.Contains("Guardi", StringComparison.Ordinal) && turn.Player == Question && turn.Reply == reply,
                "turno com a pergunta e a resposta da Guardiã", turn);
            var debug = await DebugAsync(page);
            Check(debug.Enabled && debug.Api.EndsWith("/v1/analytics/dialogs", StringComparison.Ordinal) && debug.Queued >= 1,
                "debug() expõe a fila e a API", debug);

            Check(await ReadAsync<bool>(page, "() => game.dialogLog.flush()"), "flush() enviou o lote");
            await WaitAsync(async () => (await DebugAsync(page)).Queued == 0 && (await QueueAsync(page)).Length == 0,
                "fila vazia depois da confirmação", 10_000, 500);
            var listed = await ReadAsync<JsonElement>(page,
                "async api => { const r = await fetch(api + '/v1/analytics/dialogs?limit=10'); return { status: r.status, body: await r.json() }; }",
                apiBase);
            Check(listed.GetProperty("status").GetInt32() == 200 && listed.GetProperty("body").TryGetProperty("turns", out _),
                "listagem do servidor", listed.GetRawText());
            var body = listed.GetProperty("body");
            var ids = body.GetProperty("turns").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
            Check(body.GetProperty("total").GetInt64() >= 1 && ids.Contains(turn.Id), "o servidor lista o turno recebido",
                new { ids, turn.Id });

            await page.EvaluateAsync("() => game.dialogLog.turn('Guardiã', 'teste pagehide', 'resposta pagehide', 'servidor · regras', 42)");
            await WaitAsync(async () => (await DebugAsync(page)).Queued >= 1, "turno do pagehide na fila", 10_000, 500);
            var before = await DebugAsync(page);
            traffic.Leave();
            await page.EvaluateAsync("() => window.dispatchEvent(new Event('pagehide'))");
            DialogLogState after = before;
            await WaitAsync(async () => (after = await DebugAsync(page)).Queued == 0 && !after.Sending && after.SentTotal > before.SentTotal,
                "pagehide envia e esvazia a fila", 20_000, 500);

            // Conflitos de pose na rota de comandos passam pelo observador: a mensagem do console só é
            // tolerada quando a resposta na mesma URL confirma pose/too_fast.
            var pose = await conflicts.ClassifyAsync(await traffic.ConflictNoticesAsync());
            Check(pose.Failures.Length == 0, "conflitos de comando", pose);
            var (failures, tolerated) = await traffic.VerdictAsync();
            Check(failures.Length == 0, "console e HTTP sem falhas", new { failures, tolerated, pose = pose.Tolerated });
            Log(new
            {
                build, mode = Mode, proxy = proxy is not null, reply = unavailable ? "aviso de IA indisponível" : "servidor",
                turn.Npc, turn.Source, turn.Ms, total = body.GetProperty("total").GetInt64(), sent = after.SentTotal,
                tolerated, pose = pose.Tolerated, elapsedMs = clock.ElapsedMilliseconds
            });
        }
        finally { await page.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors }); }
    }

    /// <summary>
    /// Boot e cena ativa por sondagem: no headless com SwiftShader o quadro de animação engasga.
    /// Até três rodadas com recarga, porque o runtime WASM pode estagnar por contenção de CPU na
    /// máquina compartilhada e o mesmo build sobe em segundos na seguinte.
    /// </summary>
    private static async Task WaitSceneAsync(IPage page, string url, Traffic traffic)
    {
        Exception? last = null;
        for (var round = 0; round < 3; round++)
        {
            if (round > 0)
            {
                traffic.Reset();
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
            }
            try
            {
                await WaitAsync(() => GuardAsync(() => page.EvaluateAsync<bool>(
                    "() => !!(window.game?.dialogLog && document.getElementById('game-canvas'))")), "boot do motor", 90_000, 1000);
                // No worker cameraState() devolve uma Promise: a leitura tem de esperar a resposta.
                CameraState? camera = null;
                await WaitAsync(async () => double.IsFinite((camera = await GuardAsync(() =>
                    ReadAsync<CameraState?>(page, "() => game.cameraState()")))?.Alpha ?? double.NaN), "cena ativa", 60_000, 1000);
                return;
            }
            catch (InvalidOperationException failure)
            {
                last = failure;
                Log(new { round = round + 1, falha = failure.Message, erros = traffic.Errors() });
            }
        }
        throw new InvalidOperationException($"cena ativa após 3 recargas: {string.Join(" | ", traffic.Errors())}", last);
    }

    private static Task<bool> VisibleAsync(IPage page, string selector) => GuardAsync(() => page.EvaluateAsync<bool>(
        "sel => { const el = document.querySelector(sel); return !!el && el.getBoundingClientRect().height > 0; }", selector));

    private static Task<string> LineAsync(IPage page) => GuardAsync(() => page.EvaluateAsync<string>(
        "() => (document.querySelector('.ultima-dialog__line')?.textContent ?? '').trim()"));

    private static Task<DialogEntry[]> QueueAsync(IPage page) => ReadAsync<DialogEntry[]>(
        page, "key => JSON.parse(localStorage.getItem(key) || '[]')", QueueKey);

    private static Task<DialogLogState> DebugAsync(IPage page) =>
        ReadAsync<DialogLogState>(page, "async () => await game.dialogLog.debug()");

    /// <summary>Sondagem com tolerância a falhas transitórias (navegação, motor ainda sem cena).</summary>
    private static async Task<T> GuardAsync<T>(Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception) { return default!; }
    }

    private static async Task<bool> PollAsync(Func<Task<bool>> condition, int timeoutMs, int intervalMs)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (await GuardAsync(condition) is true) return true;
            await Task.Delay(intervalMs);
        }
        return false;
    }

    private sealed record CameraState(double Alpha);
    private sealed record Position(double X, double Z);
    // Posição da Guardiã no servidor (GameWorld.NpcX/NpcZ); a conversa vale até 4,5 m.
    private static readonly Position Guardian = new(0, -14);
    private static double Distance(Position? a, Position? b) =>
        a is null || b is null ? double.MaxValue : Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    private sealed record DialogEntry(string Id, string Npc, string Player, string Reply, string Source, int Ms);
    private sealed record DialogLogState(bool Enabled, string Api, int Queued, bool Sending, int SentTotal);
    private sealed record BadResponse(int Status, string Method, string Path, long At, string? Error);

    /// <summary>
    /// Respostas HTTP a partir de 400 e mensagens de erro do console. O jogo trata algumas respostas
    /// (reentrada de sessão, limite de comandos, falta de IA) e o Chromium registra o recurso no console
    /// de qualquer jeito: cada mensagem de recurso precisa corresponder a uma resposta observada, e
    /// cada resposta precisa ter um motivo conhecido. Os conflitos da rota de comandos ficam com o
    /// CommandConflicts. O resto falha.
    /// </summary>
    private sealed class Traffic(IPage page, string apiBase, string strip)
    {
        private static readonly Regex ResourceError = new(@"^Failed to load resource: the server responded with a status of (\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex SourcePosition = new(@":\d+:\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex Noise = new("favicon|wllama|React DevTools|WebGPU|webkitExitFullscreen", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Conflict = CommandConflicts.Notice;
        private readonly ConcurrentQueue<string> _errors = new();
        private readonly ConcurrentQueue<Task<BadResponse>> _bad = new();
        private long _leftAt;

        internal Traffic Start()
        {
            page.PageError += (_, error) => _errors.Enqueue(error);
            page.Console += (_, message) =>
            {
                // No Playwright para .NET a localização vem como "url:linha:coluna"; no erro de recurso é a URL.
                if (message.Type == "error")
                    _errors.Enqueue($"{message.Text} <<{SourcePosition.Replace(message.Location ?? "", "")}>>");
            };
            page.Response += (_, response) =>
            {
                if (response.Status >= 400) _bad.Enqueue(Note(response));
            };
            return this;
        }

        /// <summary>Descarta o que uma rodada que estagnou deixou: depois da recarga só conta o novo.</summary>
        internal void Reset()
        {
            _errors.Clear();
            _bad.Clear();
        }

        /// <summary>Momento em que o ensaio derruba a sessão: o que vier depois é efeito da saída.</summary>
        internal void Leave() => _leftAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        internal string[] Errors() => [.. _errors];

        private async Task<BadResponse> Note(IResponse response)
        {
            string? error = null;
            try { error = (await response.JsonAsync())?.GetProperty("error").GetString(); }
            catch (Exception) { /* corpo ilegível conta como falha */ }
            return new(response.Status, response.Request.Method,
                response.Url.StartsWith(apiBase, StringComparison.Ordinal) ? response.Url[apiBase.Length..] : new Uri(response.Url).PathAndQuery,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), error);
        }

        internal async Task<bool> SeenAsync(int status, string path)
        {
            var seen = await Task.WhenAll(_bad);
            return seen.Any(response => response.Status == status && Route(response.Path) == path);
        }

        /// <summary>Mensagens de conflito na rota de comandos, para o observador decidir com a resposta.</summary>
        internal Task<string[]> ConflictNoticesAsync() => Task.FromResult(_errors.Where(error => Conflict.IsMatch(error)).ToArray());

        private static bool IsConflict(BadResponse response) =>
            response.Status == 409 && Route(response.Path) == "/v1/game/command";

        private string? Reason(BadResponse response)
        {
            var path = Route(response.Path);
            if (response.Status == 401 && path is "/v1/game/events" or "/v1/game/state") return "reentrada de sessão";
            if (response.Status == 401 && path == "/v1/game/command" && _leftAt > 0 && response.At >= _leftAt) return "saída da sessão";
            if (response.Status == 429 && path == "/v1/game/command") return "limite de comandos";
            if (response.Status == 503 && path == "/v1/game/garden/dialog" && response.Error == "ai_unavailable") return "servidor sem IA";
            return null;
        }

        internal async Task<(string[] Failures, string[] Tolerated)> VerdictAsync()
        {
            var bad = await Task.WhenAll(_bad);
            var failures = new List<string>();
            var tolerated = new List<string>();
            foreach (var response in bad)
            {
                if (IsConflict(response)) continue;
                if (Reason(response) is { } reason) tolerated.Add($"{response.Status} {response.Method} {Route(response.Path)}: {reason}");
                else failures.Add($"{response.Status} {response.Method} {response.Path} {response.Error}");
            }
            foreach (var error in _errors)
            {
                if (Noise.IsMatch(error) || Conflict.IsMatch(error)) continue;
                var match = ResourceError.Match(error);
                if (match.Success)
                {
                    var status = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (bad.Any(response => response.Status == status && Route(response.Path) == Route(Normalize(error)))) continue;
                }
                failures.Add(error);
            }
            return ([.. failures], [.. tolerated]);
        }

        /// <summary>Caminho da mensagem de console, na mesma base dos caminhos das respostas.</summary>
        private string Normalize(string message)
        {
            var location = message[(message.LastIndexOf("<<", StringComparison.Ordinal) + 2)..];
            location = location.EndsWith(">>", StringComparison.Ordinal) ? location[..^2] : location;
            var path = Uri.TryCreate(location, UriKind.Absolute, out var uri) ? uri.PathAndQuery : location;
            return path.StartsWith(strip, StringComparison.Ordinal) ? path[strip.Length..] : path;
        }

        private static string Route(string url) => url.Split('?')[0];
    }
}
