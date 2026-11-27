namespace Ultima.Npc;

/// <summary>
/// Agendador dos cérebros de NPC: fila de eventos com limite anti-inundação e orçamento
/// por tick (número de eventos e de comandos). Um NPC nunca bloqueia o jogo: o que não
/// cabe no orçamento fica na fila e retoma no tick seguinte (padrão da VM de scripts do
/// lineage-rust, com budget de ops por step).
/// </summary>
/// <remarks>
/// Roteamento: <see cref="PlayerInteracted"/> vai para o diálogo aberto ou para o NPC
/// que o jogador está perto (Guardiã tem prioridade sobre o Andarilho);
/// <see cref="IntroLinesChanged"/> é conteúdo da Guardiã; falas do jogador e respostas
/// vão para o diálogo aberto. Adicionar um NPC novo é registrar o cérebro aqui — o
/// loop e o orçamento não mudam.
/// </remarks>
public sealed class NpcScheduler
{
    public const string GuardianId = "guardia";
    public const string WandererId = "andarilho";

    /// <summary>Máximo de eventos aguardando tick (excedente descarta o mais antigo).</summary>
    public const int MaxQueuedEvents = 32;

    /// <summary>Eventos processados por tick no máximo.</summary>
    public const int MaxEventsPerTick = 4;

    /// <summary>Comandos acumulados por tick no máximo (corte o orçamento e retoma no próximo).</summary>
    public const int MaxCommandsPerTick = 16;

    private readonly Queue<GameEvent> _events = new();
    private readonly Dictionary<string, DialogBrain> _brains = new()
    {
        [GuardianId] = new GuardianBrain(),
        [WandererId] = new WandererBrain()
    };
    private string? _active;

    public IReadOnlyCollection<string> Npcs => _brains.Keys;
    public string SpeakerFor(string id) => _brains[id].Speaker;

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
            events++;
            foreach (var decision in Route(snapshot, e))
            {
                commands += decision.Commands.Count;
                (decisions ??= []).Add(decision);
                if (commands >= MaxCommandsPerTick) { break; }
            }
        }
        return decisions ?? (IReadOnlyList<NpcDecision>)[];
    }

    private IEnumerable<NpcDecision> Route(WorldSnapshot snapshot, GameEvent e)
    {
        switch (e)
        {
            case AnimationMarked mark:
                return [new NpcDecision([new ResolveAnimationMark(mark.Name, mark.Payload, mark.Target)], null)];
            case PlayerInteracted:
                // Diálogo aberto tem prioridade (avança/encerra); senão, o NPC mais perto
                // vira alvo — a Guardiã ganha quando os dois estão ao alcance.
                var target = _active
                    ?? (snapshot.PlayerNearGuardian ? GuardianId
                    : snapshot.PlayerNearWanderer ? WandererId : null);
                if (target is not null)
                {
                    return Decide(target, snapshot, e);
                }
                return [];
            case IntroLinesChanged:
                // Conteúdo remoto da API do jogo é da Guardiã.
                return Decide(GuardianId, snapshot, e);
            case DialogClosedByPlayer:
                if (_active is not null)
                {
                    _brains[_active].Decide(snapshot, e);   // sincroniza o cérebro do diálogo
                    _active = null;
                }
                return [];
            default:
                return _active is null ? [] : Decide(_active, snapshot, e);
        }
    }

    private IReadOnlyList<NpcDecision> Decide(string id, WorldSnapshot snapshot, GameEvent e)
    {
        var decision = _brains[id].Decide(snapshot, e);
        if (decision.Commands.Any(c => c is OpenDialog)) { _active = id; }
        else if (decision.Commands.Any(c => c is CloseDialog)) { _active = null; }
        if (decision.View is { } view) decision = decision with { View = view with { NpcId = id } };
        return decision.Commands.Count == 0 && decision.View is null ? [] : [decision];
    }
}
