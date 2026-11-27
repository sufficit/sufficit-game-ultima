namespace Ultima.Npc;

public sealed record DialogChoice(string Id, string Text);

public sealed record DialogTopic(string Id, string Prompt, string Reply);

internal static class DialogTopics
{
    // Topic IDs identify branches independently of the Portuguese strings.
    internal static IReadOnlyList<DialogTopic> Wanderer { get; } =
    [
        new("wanderer.trail", "Para onde vai a trilha?",
            "Siga a trilha para longe da clareira. O terreno sobe em direção às montanhas, e a neve marca a parte mais alta."),
        new("wanderer.return", "Onde encontro a Guardiã?",
            "Volte à clareira e procure a Guardiã perto da entrada do jardim. Ela conhece as ferramentas da Oficina.")
    ];
}
