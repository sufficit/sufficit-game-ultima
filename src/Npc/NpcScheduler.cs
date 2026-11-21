namespace Ultima.Npc;

/// <summary>
/// Agendador dos cérebros de NPC: fila de eventos com limite anti-inundação e orçamento
/// por tick (número de eventos e de comandos). Um NPC nunca bloqueia o jogo: o que não
/// cabe no orçamento fica na fila e retoma no tick seguinte (padrão da VM de scripts do
/// lineage-rust, com budget de ops por step).
/// </summary>
public sealed class NpcScheduler
{
    /// <summary>Máximo de eventos aguardando tick (excedente descarta o mais antigo).</summary>
    public const int MaxQueuedEvents = 32;

    /// <summary>Eventos processados por tick no máximo.</summary>
    public const int MaxEventsPerTick = 4;

    /// <summary>Comandos acumulados por tick no máximo (corte o orçamento e retoma no próximo).</summary>
    public const int MaxCommandsPerTick = 16;

    private readonly Queue<GameEvent> _events = new();
    private readonly GuardianBrain _guardian = new();

    public int PendingEvents => _events.Count;
    public long DroppedEvents { get; private set; }

    /// <summary>Enfileira um evento; transbordo descarta o mais antigo (anti-inundação).</summary>
    public void Submit(GameEvent e)
    {
        if (_events.Count >= MaxQueuedEvents)
        {
            _events.Dequeue();
            DroppedEvents++;
        }
        _events.Enqueue(e);
    }

    /// <summary>
    /// Processa a fila sobre um snapshot do mundo. Devolve as decisões produzidas; eventos
    /// que não couberem no orçamento permanecem para o próximo tick.
    /// </summary>
    public IReadOnlyList<NpcDecision> Tick(WorldSnapshot snapshot)
    {
        List<NpcDecision>? decisions = null;
        var events = 0;
        var commands = 0;
        while (_events.Count > 0 && events < MaxEventsPerTick && commands < MaxCommandsPerTick)
        {
            var e = _events.Dequeue();
            var decision = _guardian.Decide(snapshot, e);
            events++;
            if (decision.Commands.Count == 0 && decision.View is null) { continue; }
            commands += decision.Commands.Count;
            (decisions ??= []).Add(decision);
        }
        return decisions ?? (IReadOnlyList<NpcDecision>)[];
    }
}
