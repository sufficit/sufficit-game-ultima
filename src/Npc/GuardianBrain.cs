namespace Ultima.Npc;

/// <summary>Hosts free-form dialogue with the garden assistant.</summary>
public sealed class GuardianBrain() : DialogBrain(
    "Guardiã do Jardim",
    [new("Guardiã do Jardim", "Olá, caminhante. Como posso ajudar?")],
    []);
