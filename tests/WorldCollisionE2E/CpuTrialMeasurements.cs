using Microsoft.Playwright;

/// <summary>Samples the active game tab and Chromium process tree during a tool turn.</summary>
internal sealed class CpuTrialMeasurements : IAsyncDisposable
{
    private readonly IPage _page;
    private readonly ICDPSession _cdp;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private long? _peakPssKiB;

    private CpuTrialMeasurements(IPage page, ICDPSession cdp, long? loadedPssKiB)
    {
        _page = page;
        _cdp = cdp;
        LoadedPssKiB = loadedPssKiB;
        _sampling = SampleAsync();
    }

    private long? LoadedPssKiB { get; }

    internal static async Task<CpuTrialMeasurements> StartAsync(IBrowser browser, IPage page)
    {
        var cdp = await browser.NewBrowserCDPSessionAsync();
        var loadedPss = await ReadPssAsync(cdp);
        await page.EvaluateAsync("""
            () => {
                const sample = window.cpuTrialFrames = { count: 0, slow: 0, maxMs: 0, id: 0 };
                let last;
                function tick(now) {
                    if (last !== undefined) {
                        const delta = now - last;
                        sample.count++;
                        if (delta > 50) sample.slow++;
                        sample.maxMs = Math.max(delta, sample.maxMs);
                    }
                    last = now;
                    sample.id = requestAnimationFrame(tick);
                }
                sample.id = requestAnimationFrame(tick);
            }
            """);
        return new(page, cdp, loadedPss);
    }

    internal async Task<object> FinishAsync()
    {
        await _stop.CancelAsync();
        await _sampling;
        var frames = await BrowserTest.ReadAsync<Frames>(_page, """
            () => {
                const sample = window.cpuTrialFrames;
                cancelAnimationFrame(sample.id);
                return sample;
            }
            """);
        return new { LoadedPssKiB, peakPssKiB = _peakPssKiB, frames };
    }

    private async Task SampleAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
                if (await ReadPssAsync(_cdp) is { } pss)
                    _peakPssKiB = Math.Max(_peakPssKiB ?? 0, pss);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    // PSS includes WASM pages and apportions shared pages across processes.
    private static async Task<long?> ReadPssAsync(ICDPSession cdp)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var result = await cdp.SendAsync("SystemInfo.getProcessInfo");
        if (result is null) return null;
        long sum = 0;
        foreach (var process in result.Value.GetProperty("processInfo").EnumerateArray())
        {
            var pid = process.GetProperty("id").GetInt32();
            try
            {
                foreach (var line in File.ReadLines($"/proc/{pid}/smaps_rollup"))
                {
                    if (!line.StartsWith("Pss:", StringComparison.Ordinal)) continue;
                    if (long.TryParse(line.AsSpan(4).Trim().TrimEnd("kB").Trim(), out var size))
                        sum += size;
                    break;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return sum > 0 ? sum : null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _sampling;
        _stop.Dispose();
        await _cdp.DetachAsync();
    }

    private sealed record Frames(int Count, int Slow, double MaxMs);
}
