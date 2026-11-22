namespace Ultima.Npc;

/// <summary>
/// Cérebro da Guardiã do Jardim: a NPC que apresenta o jogo. As falas de abertura
/// podem ser trocadas em runtime pelo conteúdo remoto (<see cref="IntroLinesChanged"/>).
/// </summary>
public sealed class GuardianBrain() : DialogBrain(
    "Guardiã do Jardim",
    [
        new("Guardiã do Jardim", "Olá, caminhante. Você chegou ao jardim que existe para testar uma ideia."),
        new("Você", "Que ideia?"),
        new("Guardiã do Jardim", "Um jogo inteiro escrito em C#, rodando no seu navegador, com o mundo 3D desenhado pela sua placa de vídeo."),
        new("Guardiã do Jardim", "Escreva algo para mim. Se você ativar a IA na sua GPU, eu penso aqui mesmo, no seu navegador."),
        new("Você", "Até lá, boa trilha!")
    ]);
