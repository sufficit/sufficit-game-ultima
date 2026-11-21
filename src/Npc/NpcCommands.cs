namespace Ultima.Npc;

/// <summary>Uma fala do diálogo (quem fala e o texto).</summary>
public sealed record DialogLine(string Speaker, string Text);

/// <summary>
/// Estado do diálogo visto pela UI: a fonte da verdade é o cérebro do NPC; a UI é casca.
/// </summary>
public sealed record DialogView(
    string Speaker,
    IReadOnlyList<DialogLine> Lines,
    int CurrentLine,
    bool Thinking,
    string ReplySource);

/// <summary>Comando que o loop do jogo aplica no mundo/UI como consequência de uma decisão.</summary>
public abstract record NpcCommand;

/// <summary>Abrir o diálogo (congela o movimento do jogador).</summary>
public sealed record OpenDialog : NpcCommand;

/// <summary>Exibir (e falar em voz alta, se ligada) a linha indicada.</summary>
public sealed record ShowLine(int LineIndex) : NpcCommand;

/// <summary>Fechar o diálogo (libera o movimento).</summary>
public sealed record CloseDialog : NpcCommand;

/// <summary>O jogador falou e a resposta está sendo produzida: linha vira reticências.</summary>
public sealed record BeginThinking(int LineIndex) : NpcCommand;

/// <summary>A resposta do produtor caro chegou: substitui a linha de reticências.</summary>
public sealed record SetReply(int LineIndex, string Text, string Source) : NpcCommand;

/// <summary>
/// Decisão de um cérebro de NPC para um evento: comandos a aplicar e o estado do diálogo
/// depois deles. Custo alto de produzir (LLM) nunca acontece aqui — produtores caros
/// entregam <see cref="ReplyArrived"/> pela fila.
/// </summary>
public sealed record NpcDecision(IReadOnlyList<NpcCommand> Commands, DialogView? View)
{
    public static readonly NpcDecision Empty = new([], null);
}
