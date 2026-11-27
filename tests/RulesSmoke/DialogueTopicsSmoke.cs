using Ultima.Npc;

internal static class DialogueTopicsSmoke
{
    internal static void Run(Action<string, bool> check)
    {
        var scheduler = new NpcScheduler();
        var garden = new WorldSnapshot(0, 0, true, false, false, false, 1000);
        var trail = new WorldSnapshot(5, 0, false, true, false, false, 1000);
        var displayed = new HashSet<string>(StringComparer.Ordinal);
        scheduler.Submit(new PlayerInteracted(990));
        check("The Guardian opens with no preset topics", scheduler.Tick(garden).Single().View!.Choices.Count == 0);
        scheduler.Submit(new DialogClosedByPlayer(995));
        scheduler.Tick(garden);
        foreach (var (snapshot, prefix, total) in new[] { (trail, "wanderer.", 2) })
        {
            scheduler.Submit(new PlayerInteracted(1000));
            var opening = scheduler.Tick(snapshot).Single().View!;
            check($"{prefix} exposes its local choices without AI", opening.Choices.Count == total &&
                opening.Choices.All(choice => choice.Id.StartsWith(prefix, StringComparison.Ordinal) &&
                    choice.Text.Length > 0 && displayed.Add(choice.Id)));

            scheduler.Submit(new DialogTopicSelected(1010, "missing"));
            check($"{prefix} rejects an unknown topic", scheduler.Tick(snapshot).Count == 0);

            foreach (var choice in opening.Choices)
            {
                scheduler.Submit(new DialogTopicSelected(1020, choice.Id));
                var selected = scheduler.Tick(snapshot).Single();
                var view = selected.View!;
                check($"{choice.Id} displays its reply and returns to the topics",
                    selected.Commands.Single() is ShowLine line && line.LineIndex == view.CurrentLine &&
                    view.Lines[^1].Text.Length > 0 && view.Lines[^1].Speaker == opening.Speaker &&
                    view.Lines[^2].Text == choice.Text && view.Choices.SequenceEqual(opening.Choices) &&
                    !view.Thinking && view.ReplySource.Length == 0);
            }

            scheduler.Submit(new PlayerSpoke(1030, "Olá!"));
            var thinking = scheduler.Tick(snapshot).Single().View!;
            scheduler.Submit(new DialogTopicSelected(1031, opening.Choices[0].Id));
            check($"{prefix} blocks topics while a free-text reply is pending",
                thinking.Choices.Count == 0 && scheduler.Tick(snapshot).Count == 0);
            scheduler.Submit(new ReplyArrived(1032, "Olá!", "test"));
            check($"{prefix} restores choices after a free-text reply",
                scheduler.Tick(snapshot).Single().View!.Choices.SequenceEqual(opening.Choices));

            scheduler.Submit(new DialogClosedByPlayer(1040));
            scheduler.Submit(new DialogTopicSelected(1041, opening.Choices[0].Id));
            check($"{prefix} ignores topic events after closing", scheduler.Tick(snapshot).Count == 0);
        }
    }
}
