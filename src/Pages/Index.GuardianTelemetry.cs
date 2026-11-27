using Microsoft.AspNetCore.Components;
using Ultima.Browser;

namespace Ultima.Pages;

public partial class Index
{
    [Inject] private GuardianTelemetry QualityTelemetry { get; set; } = default!;
    private string? _activeGuardianCpuModel;
    private GuardianRuntime _proposalRuntime = new("local", "none");

    private readonly record struct GuardianRuntime(string Engine, string Model);

    private GuardianRuntime CpuRuntime() =>
        new("cpu", _activeGuardianCpuModel is "lfm350" or "qwen05tools" ? _activeGuardianCpuModel : "unknown");

    private void DiscardGuardianProposal()
    {
        if (_guardianProposal is null) return;
        _guardianProposal = null;
        _ = QualityTelemetry.RecordAsync(_proposalRuntime.Engine, _proposalRuntime.Model, "dismissed");
    }
}
