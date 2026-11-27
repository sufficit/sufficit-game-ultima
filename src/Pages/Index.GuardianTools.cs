using Microsoft.JSInterop;
using Ultima.Browser;

namespace Ultima.Pages;

public partial class Index
{
    private string _wandererDraft = string.Empty;
    private string ConversationDraft => GuardianOpen ? _dialogDraft : _wandererDraft;

    private void UpdateDialogueDraft(string value)
    {
        if (GuardianBusy || ConversationDraft == value) return;
        if (GuardianOpen) _dialogDraft = value;
        else _wandererDraft = value;
    }

    private async Task<GuardianTurnResult> RunGuardianTurnAsync(string text,
        GardenModelProposal? pending, Action<GuardianRuntime> selectRuntime, CancellationToken token)
    {
        await RefreshLocalAiAsync(token);
        if (_aiState != "ready" && _aiCpuState == "off")
            await ResumeLocalAiAsync("aiCpu", token);

        var gpu = _aiState == "ready";
        var cpu = _aiCpuState == "ready";
        var cpuRuntime = CpuRuntime();
        selectRuntime(gpu ? new("gpu", "unknown") : cpu ? cpuRuntime : new("server", "unknown"));
        var result = await WorkshopService.TalkToGuardianAsync(text, gpu, cpu, pending, token);
        // Offline dialogue can fall back from GPU to CPU.
        if (result.Source == GuardianGenius.SourcePrefix + " · CPU") selectRuntime(cpuRuntime);
        if (result.Source == GuardianGenius.SourcePrefix && !result.KeepDraft) selectRuntime(new("local", "none"));
        return result;
    }

    private async Task ResumeLocalAiAsync(string engine, CancellationToken token)
    {
        await JS.InvokeVoidAsync($"game.{engine}.resume", token);
        await RefreshLocalAiAsync(token);
    }
}
