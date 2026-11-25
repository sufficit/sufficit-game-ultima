namespace Ultima.Npc;

/// <summary>Gameplay event queued by input, networking, or a language model.</summary>
public abstract record GameEvent(long NowMs);

/// <summary>The player interacted with a character (E or A).</summary>
public sealed record PlayerInteracted(long NowMs) : GameEvent(NowMs);

/// <summary>The player closed the dialogue (Esc or B).</summary>
public sealed record DialogClosedByPlayer(long NowMs) : GameEvent(NowMs);

/// <summary>The player submitted free text in the current dialogue.</summary>
public sealed record PlayerSpoke(long NowMs, string Text) : GameEvent(NowMs);

/// <summary>The player selected an authored topic in the current dialogue.</summary>
public sealed record DialogTopicSelected(long NowMs, string Id) : GameEvent(NowMs);

/// <summary>
/// An asynchronous reply arrived from an AI provider, the game API, or a local fallback.
/// </summary>
public sealed record ReplyArrived(long NowMs, string Reply, string Source) : GameEvent(NowMs);

/// <summary>The game API supplied new opening lines.</summary>
public sealed record IntroLinesChanged(long NowMs, string Speaker, string[] Lines) : GameEvent(NowMs);

public sealed record AnimationMarked(long NowMs, string Name, string Payload, System.Text.Json.JsonElement Target) : GameEvent(NowMs);
