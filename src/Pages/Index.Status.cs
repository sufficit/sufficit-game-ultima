using Sufficit.Blazor.UI.Components;

namespace Ultima.Pages;

// Status card helpers that depend only on the AI engine state. They live apart from the page
// (Index.razor) so that every repository file stays within its line limit.
public partial class Index
{
    // --- What the AI is doing now, summarized for the status card (even collapsed) ---
    // Shown only when something happens or fails; "off", "checking", "unsupported" and "ready"
    // stay in the AI panel. Details and "Try again" remain in the panel.
    private sealed record AiActivity(string Label, double Progress, bool ShowProgress, Sufficit.Blazor.UI.Components.SUITone Tone);

    private AiActivity? CurrentAiActivity()
        => AiActivityOf(_aiState, _aiProgress, "GPU") ?? AiActivityOf(_aiCpuState, _aiCpuProgress, "CPU");

    private static AiActivity? AiActivityOf(string state, double progress, string engine)
    {
        var pct = (int)Math.Round(progress * 100);
        return state switch
        {
            "model" or "downloading" => new AiActivity($"Baixando IA ({engine}) · {pct}%", progress, true, SUITone.Info),
            "loading" or "compiling" => new AiActivity($"Preparando IA ({engine})…", progress, true, SUITone.Info),
            "thinking" => new AiActivity($"Guardiã pensando ({engine})…", progress, false, SUITone.Info),
            "stalled" => new AiActivity($"IA travada em {pct}% ({engine})", progress, true, SUITone.Warning),
            "error" => new AiActivity($"Erro na IA ({engine})", progress, false, SUITone.Danger),
            _ => null,
        };
    }
}
