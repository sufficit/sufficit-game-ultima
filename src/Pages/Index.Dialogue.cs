using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ultima.Browser;
using Ultima.Npc;
using Ultima.Windows;

namespace Ultima.Pages;

public partial class Index
{
    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private WorldSnapshot Snapshot() => new(_posX, _posZ, _near, _nearWanderer, _view is not null, _aiState == "ready", NowMs());

    private void Dispatch(GameEvent e)
    {
        _npc.Submit(e);
        RunNpcTick();
    }

    // Each movement tick also drains events left over from the previous budget.
    private void RunNpcTick()
    {
        if (_npc.PendingEvents == 0) { return; }
        foreach (var decision in _npc.Tick(Snapshot()))
        {
            ApplyDecision(decision);
        }
    }

    private void ApplyDecision(NpcDecision decision)
    {
        if (decision.View is not null)
        {
            _view = decision.View.CurrentLine >= 0 ? decision.View : null;
        }
        foreach (var command in decision.Commands)
        {
            switch (command)
            {
                case ResolveAnimationMark mark:
                    _ = ResolveAnimationMarkAsync(mark);
                    break;
                case OpenDialog:
                    _menuOpen = false;   // the phone menu sheet must not cover the conversation
                    _conversationState.ArchivedTurns = Dialogs.HistoryFor(decision.View!.Speaker);
                    _ = SetFrozen(true);
                    if (GuardianOpen && _guardianTurnText is { } pending)
                        _npc.Submit(new PlayerSpoke(NowMs(), pending));
                    break;
                case Ultima.Npc.CloseDialog:
                    _view = null;
                    _ = JS.InvokeVoidAsync("game.voice.stopDictation");
                    _ = SetFrozen(false);
                    break;
                case ShowLine(int index):
                    _ = SpeakLineAsync(decision.View!.Lines[index]);
                    break;
                case SetReply(int index, var text, _):
                    _ = SpeakLineAsync(new DialogLine(decision.View!.Lines[index].Speaker, text));
                    // Free-text replies use the existing dialogue history and analytics pipeline.
                    try
                    {
                        var dlg = decision.View;
                        var playerLine = index > 0 && dlg.Lines[index - 1].Speaker == "Você"
                            ? dlg.Lines[index - 1].Text : string.Empty;
                        var ms = _turnStartedMs > 0 ? (long)(NowMs() - _turnStartedMs) : 0;
                        _ = Dialogs.RecordAsync(dlg.Speaker, playerLine, text, dlg.ReplySource, ms,
                            localOnly: dlg.ReplySource.StartsWith(GuardianGenius.SourcePrefix, StringComparison.Ordinal));
                    }
                    catch (JSException) { /* History is optional and must not interrupt the conversation. */ }
                    break;
            }
        }
        StateHasChanged();
    }

    private async Task ResolveAnimationMarkAsync(ResolveAnimationMark mark)
    {
        if (mark.Name == "hit" && mark.Payload == "collect" &&
            mark.Target.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            try
            {
                var target = System.Text.Json.JsonSerializer.Deserialize<AnimationCollectionTarget>(
                    mark.Target, AnimationTargetJson.Default.AnimationCollectionTarget);
                if (target?.Resource is not null) await BackpackState.CollectAsync(target.Resource, target.Point);
            }
            catch (System.Text.Json.JsonException) { }
        }
        else if (mark.Name == "sound")
        {
            await JS.InvokeVoidAsync("game.animationSound", mark.Payload);
        }
    }

    private async Task OnInteractAsync()
    {
        if (_view is null && !_near && !_nearWanderer && _nearObject is { } label)
        {
            Snackbar.Info($"Você examinou: {label}");
        }
        else
        {
            var wasOpen = GuardianOpen;
            Dispatch(new PlayerInteracted(NowMs()));
            if (!wasOpen && GuardianOpen)
            {
                await RefreshLocalAiAsync(_browserLifetime.Token);
                StateHasChanged();
            }
        }
    }

    private string? _guardianTurnText;
    private GardenModelProposal? _guardianProposal;
    private bool _journalOpen;
    private bool _windowContrast;
    private readonly WindowState _conversationState = new();
    private readonly WindowState _journalState = new();

    private ElementReference _canvas;
    private bool _windowWasOpen;
    // Window on top of the screen stack: a conversation with a character, or the journal.
    private bool WindowOpen => _view is not null || _journalOpen;

    // On close, the focused element left the DOM: the focus returns to the canvas (keyboard and gamepad stay in the game).
    private async Task FocusCanvasAsync()
    {
        try { await JS.InvokeVoidAsync("game.browser.focus", _canvas); }
        catch (JSException) { /* page reloading or being disposed */ }
    }

    private bool _testingGuardianAi;
    private string _guardianAiTest = "";
    private bool GuardianOpen => _view is { Speaker: not "Andarilho" };
    private bool GuardianBusy => _view?.Thinking == true || (GuardianOpen && (_testingGuardianAi || _guardianTurnText is not null));
    private static bool AiLoading(string state) => state is "checking" or "downloading" or "compiling" or "loading" or "thinking";
    private sealed record LocalAiStatus(string State, double Progress, string Detail, string? Model, double? LastReplyMs, string? ModelKey);

    private async Task LoadLocalAiAsync(string engine, string action)
    {
        await JS.InvokeVoidAsync($"game.{engine}.{action}", _browserLifetime.Token);
        await RefreshLocalAiAsync(_browserLifetime.Token);
    }

    private async Task RefreshLocalAiAsync(CancellationToken token)
    {
        var gpu = JS.InvokeAsync<LocalAiStatus>("game.ai.status", token).AsTask();
        var cpu = JS.InvokeAsync<LocalAiStatus>("game.aiCpu.status", token).AsTask();
        await Task.WhenAll(gpu, cpu);
        var gpuStatus = await gpu;
        var cpuStatus = await cpu;
        (_aiState, _aiProgress, _aiDetail) = (gpuStatus.State, gpuStatus.Progress, gpuStatus.Detail);
        (_aiCpuState, _aiCpuProgress, _aiCpuDetail) = (cpuStatus.State, cpuStatus.Progress, cpuStatus.Detail);
        _activeGuardianCpuModel = cpuStatus.ModelKey;
    }

    private string GuardianAiStatus()
    {
        if (_aiState == "ready" || _aiCpuState == "ready")
            return _aiState == "ready" ? "IA local pronta na GPU." : "IA local pronta na CPU.";
        if (AiLoading(_aiState)) return _aiDetail;
        if (AiLoading(_aiCpuState)) return _aiCpuDetail;
        if (_aiState is "error" or "stalled") return _aiDetail;
        if (_aiCpuState is "error" or "stalled") return _aiCpuDetail;
        return "Ative a IA local para conversar mesmo sem conexão com o servidor.";
    }

    private async Task TestGuardianAiAsync()
    {
        if (!GuardianOpen || GuardianBusy) return;
        _testingGuardianAi = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_browserLifetime.Token);
            await RefreshLocalAiAsync(timeout.Token);
            var engine = _aiState == "ready" ? "ai" : _aiCpuState == "ready" ? "aiCpu" : null;
            if (engine is null)
            {
                _guardianAiTest = "Aguarde o modelo local ficar pronto para testar.";
                return;
            }
            _guardianAiTest = "Testando a resposta da IA local…";
            StateHasChanged();
            timeout.CancelAfter(TimeSpan.FromSeconds(engine == "ai" ? 60 : 120));
            var text = await JS.InvokeAsync<string?>($"game.{engine}.garden", timeout.Token,
                "Responda com uma saudação curta em português.", "Olá!", 32);
            _guardianAiTest = !string.IsNullOrWhiteSpace(text)
                ? "A IA local respondeu ao teste."
                : "A IA local não respondeu. Tente novamente.";
        }
        catch (Exception ex) when (ex is JSException or OperationCanceledException)
        {
            _guardianAiTest = "O teste da IA local não foi concluído. Tente novamente.";
        }
        finally { _testingGuardianAi = false; }
    }

    private WindowState ConversationState()
    {
        var view = _view!;
        _conversationState.Conversation = view;
        var values = _conversationState.Text;
        values["guardianAi"] = GuardianOpen ? GuardianAiStatus() : "";
        values["enableGpu"] = GuardianOpen && !AiLoading(_aiState) && _aiState is not ("ready" or "unsupported")
            ? _aiState is "error" or "stalled" ? "Tentar GPU novamente" : "Ativar IA na GPU" : "";
        values["enableCpu"] = GuardianOpen && !AiLoading(_aiCpuState) && _aiCpuState is not ("ready" or "unsupported")
            ? _aiCpuState is "error" or "stalled" ? "Tentar CPU novamente" : "Ativar IA na CPU" : "";
        values["busy"] = GuardianBusy ? "1" : "";
        values["thinking"] = view.Thinking ? "1" : "";
        values["testDisabled"] = GuardianBusy || (_aiState != "ready" && _aiCpuState != "ready") ? "1" : "";
        values["aiTest"] = GuardianOpen ? _guardianAiTest : "";
        values["draft"] = ConversationDraft;
        values["gardenProposal"] = GuardianOpen && _guardianProposal is not null ? "1" : "";
        BindDiagnosticReport(values);
        values["dictation"] = _dictating ? "Parar ditado" : "Ditar";
        values["speechError"] = _speechError;
        values["advance"] = view.CurrentLine + 1 < view.Lines.Count ? "Continuar · E/A" : "Encerrar · E/A";
        return _conversationState;
    }

    private void EditWindow((string Key, string Value) edit)
    {
        if (edit.Key == "draft") UpdateDialogueDraft(edit.Value);
        else if (edit.Key == "reportDraft") _reportDraft = edit.Value;
    }

    private void OpenJournal()
    {
        _panelOpen = false;
        _menuOpen = false;   // phone: the menu sheet goes away and the journal stays alone (modal window)
        _journalOpen = true;
        var entries = Dialogs.RecentEntries();
        _journalState.Lists["entries"] = entries;
        _journalState.Text["empty"] = entries.Count == 0 ? "Suas conversas com personagens aparecem aqui." : "";
        _journalState.Text["hasEntries"] = entries.Count == 0 ? "" : "true";
        UpdateWindowThemeLabel();
    }

    private void UpdateWindowThemeLabel() =>
        _journalState.Text["theme"] = _windowContrast ? "Tema padrão" : "Mais contraste";

    private Task DispatchWindowActionAsync(string action)
    {
        const string topicPrefix = "topic:";
        if (action.StartsWith(topicPrefix, StringComparison.Ordinal))
        {
            if (_view is not null && !GuardianBusy)
            {
                if (GuardianOpen) DiscardGuardianProposal();
                Dispatch(new DialogTopicSelected(NowMs(), action[topicPrefix.Length..]));
            }
            return Task.CompletedTask;
        }

        switch (action)
        {
            case "report-toggle":
                _reportOpen = !_reportOpen;
                _reportStatus = "";
                break;
            case "report-send": return SendDiagnosticReportAsync();
            case "advance": return AdvanceAsync();
            case "speak": return SubmitDialogAsync();
            case "dictate" when !GuardianBusy: return ToggleDictationAsync();
            case "listen" when _view is { Thinking: false } dialog:
                var line = dialog.Lines[dialog.CurrentLine];
                return JS.InvokeVoidAsync("game.voice.speak", line.Text, line.Speaker).AsTask();
            case "guardian-gpu" when GuardianOpen && !GuardianBusy:
                _guardianAiTest = "";
                return _aiState is "error" or "stalled" ? RetryAiAsync() : EnableAiAsync();
            case "guardian-cpu" when GuardianOpen && !GuardianBusy:
                _guardianAiTest = "";
                return _aiCpuState is "error" or "stalled" ? RetryAiCpuAsync() : EnableAiCpuAsync();
            case "guardian-test": return TestGuardianAiAsync();
            case "garden-tool-apply" when _guardianProposal is { } proposal && GuardianOpen && !GuardianBusy:
                return SubmitDialogAsync(confirmedProposal: proposal);
            case "garden-tool-cancel":
                DiscardGuardianProposal();
                break;
            case "light-garden" when GuardianOpen && !GuardianBusy:
                return SubmitDialogAsync(lightGarden: true);
            case "suggest-path" when GuardianOpen && !GuardianBusy:
                UpdateDialogueDraft("Crie uma trilha de pedra ligando a entrada do jardim à clareira.");
                break;
            case "suggest-flowers" when GuardianOpen && !GuardianBusy:
                UpdateDialogueDraft("Plante flores nas bordas do caminho do jardim.");
                break;
            case "close":
                if (_view is not null) CloseDialog();
                else _journalOpen = false;
                break;
            case "theme":
                _windowContrast = !_windowContrast;
                UpdateWindowThemeLabel();
                return SaveLocalPreferencesAsync();
        }
        return Task.CompletedTask;
    }

    private Task AdvanceAsync() { Dispatch(new PlayerInteracted(NowMs())); return Task.CompletedTask; }

    private void CloseDialog()
    {
        if (_view is null) return;
        _npc.Submit(new DialogClosedByPlayer(NowMs()));
        _view = null;
        _ = JS.InvokeVoidAsync("game.voice.stopDictation");
        _ = SetFrozen(false);
        StateHasChanged();
    }

    private async Task SpeakLineAsync(DialogLine line)
    {
        await JS.InvokeVoidAsync("game.characterDialogueLine", line.Speaker, line.Text.Length);
        if (_speakEnabled) await JS.InvokeVoidAsync("game.voice.speak", line.Text, line.Speaker);
    }

    private async Task OnSpeakToggled(bool enabled)
    {
        _speakEnabled = enabled;
        await JS.InvokeVoidAsync("game.voice.setSpeakPref", _speakEnabled);
        if (_speakEnabled && _view is not null) await SpeakLineAsync(_view.Lines[_view.CurrentLine]);
    }

    private async Task ToggleDictationAsync()
    {
        _speechError = string.Empty;
        if (_dictating) await JS.InvokeVoidAsync("game.voice.stopDictation");
        else await JS.InvokeVoidAsync("game.voice.startDictation");
    }

}
