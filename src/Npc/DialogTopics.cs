namespace Ultima.Npc;

public sealed record DialogChoice(string Id, string Text);

public sealed record DialogTopic(string Id, string Prompt, string Reply);

internal static class DialogTopics
{
    // Topic IDs identify branches independently of the Portuguese strings.
    internal static IReadOnlyList<DialogTopic> Guardian { get; } =
    [
        new("guardian.garden", "O que posso fazer no jardim?",
            "Explore a clareira e siga o caminho entre as árvores. A Oficina do mundo tem ferramentas para pintar o solo, escavar e posicionar objetos."),
        new("guardian.collect", "Como recolho materiais?",
            "Abra a Mochila no painel e escolha Coletar. Aponte para um objeto ou trecho de solo próximo e clique ou toque quando o alvo estiver iluminado."),
        new("guardian.lights", "Como ilumino a entrada?",
            "Use Iluminar o jardim nesta janela para pedir luminárias na entrada. Em um mundo compartilhado, as mudanças passam pela API do jogo.")
    ];

    internal static IReadOnlyList<DialogTopic> Wanderer { get; } =
    [
        new("wanderer.trail", "Para onde vai a trilha?",
            "Siga a trilha para longe da clareira. O terreno sobe em direção às montanhas, e a neve marca a parte mais alta."),
        new("wanderer.return", "Onde encontro a Guardiã?",
            "Volte à clareira e procure a Guardiã perto da entrada do jardim. Ela conhece as ferramentas da Oficina.")
    ];
}
