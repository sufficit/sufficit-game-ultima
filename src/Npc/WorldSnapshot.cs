namespace Ultima.Npc;

/// <summary>
/// Projeção imutável do mundo no instante do tick. Os cérebros de NPC decidem somente
/// sobre snapshots — nunca leem nem mutam o mundo vivo (padrão do bot do lineage-rust:
/// <c>Brain::decide(env)</c> sobre <c>Env</c>). Adicionar um NPC novo é acrescentar aqui
/// a proximidade correspondente e registrar o cérebro no <see cref="NpcScheduler"/>.
/// </summary>
public sealed record WorldSnapshot(
    double PlayerX,
    double PlayerZ,
    bool PlayerNearGuardian,
    bool PlayerNearWanderer,
    bool DialogOpen,
    bool AiReady,
    long NowMs);
