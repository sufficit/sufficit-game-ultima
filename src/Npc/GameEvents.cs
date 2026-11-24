namespace Ultima.Npc;

/// <summary>Evento de gameplay que entra na fila do agendador (origem: input, rede, LLM).</summary>
public abstract record GameEvent(long NowMs);

/// <summary>O jogador acionou a interação (tecla E, botão A do controle/toque).</summary>
public sealed record PlayerInteracted(long NowMs) : GameEvent(NowMs);

/// <summary>O jogador fechou o diálogo (Esc, botão B).</summary>
public sealed record DialogClosedByPlayer(long NowMs) : GameEvent(NowMs);

/// <summary>O jogador enviou uma mensagem no diálogo.</summary>
public sealed record PlayerSpoke(long NowMs, string Text) : GameEvent(NowMs);

/// <summary>
/// Uma resposta chegou de um produtor caro (WebLLM na GPU do jogador, API do jogo ou
/// falas locais). A resposta é assíncrona: vira evento e é aplicada em um tick futuro.
/// </summary>
public sealed record ReplyArrived(long NowMs, string Reply, string Source) : GameEvent(NowMs);

/// <summary>As falas de abertura mudaram (conteúdo remoto da API do jogo).</summary>
public sealed record IntroLinesChanged(long NowMs, string Speaker, string[] Lines) : GameEvent(NowMs);

public sealed record AnimationMarked(long NowMs, string Name, string Payload, System.Text.Json.JsonElement Target) : GameEvent(NowMs);
