namespace Ultima.Npc;

/// <summary>Shares directions and stories about the trail.</summary>
public sealed class WandererBrain() : DialogBrain(
    "Andarilho",
    [
        new("Andarilho", "Ei, viajante! As montanhas pareciam menores na última vez que passei por aqui."),
        new("Andarilho", "Sigo a trilha até a neve e volto antes do anoitecer; o jardim sempre me recebe."),
        new("Andarilho", "Se a Guardiã perguntar por mim: sigo bem, só um pouco cansado dos pés.")
    ], DialogTopics.Wanderer);
