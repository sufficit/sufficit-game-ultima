using Microsoft.AspNetCore.Components;
using Ultima.Browser;

namespace Ultima.Pages;

public partial class Index
{
    [Inject] private GuardianGenius GeniusConversations { get; set; } = default!;
    private int _conversationOpening;

    private async Task LoadConversationHistoryAsync(string npc, int opening)
    {
        var history = await GeniusConversations.HistoryAsync(npc, _browserLifetime.Token);
        if (_conversationOpening != opening || _view?.NpcId != npc || _browserLifetime.IsCancellationRequested) return;
        _conversationState.ArchivedTurns = history;
        StateHasChanged();
    }
}
