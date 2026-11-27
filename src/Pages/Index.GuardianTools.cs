using Ultima.Browser;

namespace Ultima.Pages;

public partial class Index
{
    private void UpdateDialogueDraft(string value)
    {
        if (_view?.Thinking == true || _dialogDraft == value) return;
        _guardianProposal = null;
        _dialogDraft = value;
    }

    private async Task<GuardianTurnResult> RunGuardianTurnAsync(string text, bool lightGarden,
        GardenModelProposal? confirmedProposal, CancellationToken token)
    {
        if (confirmedProposal is not null) return await WorkshopService.ApplyGardenToolAsync(confirmedProposal, token);
        if (lightGarden) return await WorkshopService.LightGardenAsync(token);

        await RefreshLocalAiAsync(token);
        if (_activeCpuModel == "qwen05tools")
        {
            if (_aiCpuState != "ready")
                return GuardianTurnResult.Retry("Aguarde a IA de ferramentas ficar pronta na CPU.", GuardianGenius.SourcePrefix);
            return await WorkshopService.ProposeGardenToolAsync(text, token);
        }

        return await WorkshopService.TalkToGuardianAsync(text, _aiState == "ready", _aiCpuState == "ready", token);
    }
}
