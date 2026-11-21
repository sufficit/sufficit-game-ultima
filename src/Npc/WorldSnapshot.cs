namespace Ultima.Npc;

/// <summary>
/// Projeção imutável do mundo no instante do tick. Os cérebros de NPC decidem somente
/// sobre snapshots — nunca leem nem mutam o mundo vivo (padrão do bot do lineage-rust:
/// <c>Brain::decide(env)</c> sobre <c>Env</c>).
/// </summary>
public sealed record WorldSnapshot(
    double PlayerX,
    double PlayerZ,
    bool PlayerNearGuardian,
    bool DialogOpen,
    bool AiReady,
    long NowMs);
