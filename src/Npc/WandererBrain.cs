namespace Ultima.Npc;

/// <summary>
/// Cérebro do Andarilho: segundo NPC do jardim, um caminhante que circula pela
/// clareira inicial. Prova que o desenho de decisões escala — o agendador e o loop
/// não mudam ao adicionar NPCs, só entra outro cérebro no registro.
/// </summary>
public sealed class WandererBrain() : DialogBrain(
    "Andarilho",
    [
        new("Andarilho", "Ei, viajante! As montanhas pareciam menores na última vez que passei por aqui."),
        new("Andarilho", "Sigo a trilha até a neve e volto antes do anoitecer; o jardim sempre me recebe."),
        new("Andarilho", "Se a Guardiã perguntar por mim: sigo bem, só um pouco cansado dos pés.")
    ]);
