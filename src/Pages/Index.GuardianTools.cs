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
        if (GuardianOpen)
        {
            _guardianProposal = null;
            _dialogDraft = value;
        }
        else _wandererDraft = value;
    }

    private async Task<GuardianTurnResult> RunGuardianTurnAsync(string text, bool lightGarden,
        GardenModelProposal? confirmedProposal, CancellationToken token)
    {
        if (confirmedProposal is not null) return await WorkshopService.ApplyGardenToolAsync(confirmedProposal, token);
        if (lightGarden) return await WorkshopService.LightGardenAsync(token);

        await RefreshLocalAiAsync(token);
        if (_aiState != "ready" && _aiCpuState == "off")
            await ResumeLocalAiAsync("aiCpu", token);

        return await WorkshopService.TalkToGuardianAsync(text, _aiState == "ready", _aiCpuState == "ready", token);
    }

    private async Task ResumeLocalAiAsync(string engine, CancellationToken token)
    {
        await JS.InvokeVoidAsync($"game.{engine}.resume", token);
        await RefreshLocalAiAsync(token);
    }
}
