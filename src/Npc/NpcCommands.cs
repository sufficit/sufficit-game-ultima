namespace Ultima.Npc;

/// <summary>A spoken line with its speaker and text.</summary>
public sealed record DialogLine(string Speaker, string Text);

/// <summary>Dialogue state supplied to the UI by the NPC brain.</summary>
public sealed record DialogView(
    string Speaker,
    IReadOnlyList<DialogLine> Lines,
    int CurrentLine,
    bool Thinking,
    string ReplySource,
    IReadOnlyList<DialogChoice> Choices);

/// <summary>An effect that the game applies after an NPC decision.</summary>
public abstract record NpcCommand;

/// <summary>Open the dialogue and suspend player movement.</summary>
public sealed record OpenDialog : NpcCommand;

/// <summary>Display the line and read it aloud when speech is enabled.</summary>
public sealed record ShowLine(int LineIndex) : NpcCommand;

/// <summary>Close the dialogue and resume player movement.</summary>
public sealed record CloseDialog : NpcCommand;

/// <summary>Show that a reply to the player is pending.</summary>
public sealed record BeginThinking(int LineIndex) : NpcCommand;

/// <summary>Replace the pending line with the reply.</summary>
public sealed record SetReply(int LineIndex, string Text, string Source) : NpcCommand;

/// <summary>
/// Commands and dialogue state produced in response to an event.
/// Asynchronous providers submit <see cref="ReplyArrived"/> through the queue.
/// </summary>
public sealed record NpcDecision(IReadOnlyList<NpcCommand> Commands, DialogView? View)
{
    public static readonly NpcDecision Empty = new([], null);
}

public sealed record ResolveAnimationMark(string Name, string Payload, System.Text.Json.JsonElement Target) : NpcCommand;
