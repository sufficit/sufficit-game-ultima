using Microsoft.JSInterop;
using Ultima.Browser;
using Ultima.Npc;

namespace Ultima.Pages;

public partial class Index
{
    private async Task SubmitDialogAsync(bool lightGarden = false, GardenModelProposal? confirmedProposal = null)
    {
        var text = confirmedProposal is not null ? "Confirmo a alteração." :
            lightGarden ? "Ilumine a entrada do jardim." : ConversationDraft.Trim();
        if (_view is null || GuardianBusy || text.Length == 0) return;
        var speaker = _view.Speaker;
        var runtime = confirmedProposal is not null ? _proposalRuntime
            : lightGarden ? new("shortcut", "none") : new("local", "none");
        if (GuardianOpen)
        {
            if (confirmedProposal is not null) _guardianProposal = null;
            else DiscardGuardianProposal();
        }
        if (text.Length > 300) text = text[..300];
        _speechError = string.Empty;
        var started = _turnStartedMs = NowMs();
        _ = JS.InvokeVoidAsync("game.characterDialogueLine", "Você", text.Length);
        Dispatch(new PlayerSpoke(NowMs(), text));
        if (_view?.Speaker == "Andarilho")
        {
            _wandererDraft = string.Empty;
            Dispatch(new ReplyArrived(NowMs(), "Sobre o jardim, fale com a Guardiã.", "falas locais do andarilho"));
            return;
        }

        // A turn belongs to the browser session. Closing the modal leaves its work and proposal intact.
        _guardianTurnText = text;
        var token = _browserLifetime.Token;
        GuardianTurnResult result;
        try
        {
            result = confirmedProposal is not null
                ? await WorkshopService.ApplyGardenToolAsync(confirmedProposal, token)
                : lightGarden ? await WorkshopService.LightGardenAsync(token)
                : await RunGuardianTurnAsync(text, selected => runtime = selected, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is JSException or OperationCanceledException)
        {
            result = GuardianTurnResult.Retry("Não consegui concluir o pedido. Tente novamente.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        finally { _guardianTurnText = null; }
        if (token.IsCancellationRequested) return;

        if (!lightGarden && !result.KeepDraft) _dialogDraft = string.Empty;
        _guardianProposal = result.Proposal;
        if (result.Proposal is not null) _proposalRuntime = runtime;
        var outcome = result.QualityOutcome ?? (confirmedProposal is not null || lightGarden
            ? result.KeepDraft ? "apply_failed" : "applied"
            : result.Proposal is not null ? "proposal" : result.KeepDraft ? "turn_failed" : "reply");
        _ = QualityTelemetry.RecordAsync(runtime.Engine, runtime.Model, outcome, NowMs() - started);
        if (GuardianOpen)
        {
            _turnStartedMs = started;
            Dispatch(new ReplyArrived(NowMs(), result.Reply, result.Source));
            return;
        }

        if (!WindowOpen)
            Snackbar.Info(result.Proposal is null
                ? "A Guardiã respondeu. Abra a conversa para ler."
                : "A Guardiã preparou uma proposta. Abra a conversa para revisar.");
        try
        {
            await Dialogs.RecordAsync(speaker, text, result.Reply, result.Source, NowMs() - started,
                localOnly: result.Source.StartsWith(GuardianGenius.SourcePrefix, StringComparison.Ordinal));
            if (GuardianOpen) _conversationState.ArchivedTurns = Dialogs.HistoryFor(speaker);
        }
        catch (JSException) { /* The dialogue history is optional. */ }
        if (!token.IsCancellationRequested) StateHasChanged();
    }
}
