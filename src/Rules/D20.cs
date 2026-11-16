using System;

namespace Ultima.Rules;

/// <summary>Estado de Vantagem/Desvantagem de uma rolagem (SRD 5.2.1, "D20 Tests").</summary>
public enum RollMode
{
    Normal,
    Advantage,
    Disadvantage
}

public enum D20Kind
{
    /// <summary>Teste de atributo ou salvaguarda: 20 e 1 naturais não têm efeito automático.</summary>
    Check,
    /// <summary>Jogada de ataque: 20 natural acerta (Acerto Crítico) e 1 natural erra.</summary>
    Attack
}

public sealed record D20Result(
    int[] Dice,
    int Natural,
    RollMode Mode,
    int Modifier,
    int Total,
    int TargetNumber,
    bool Success,
    bool CriticalHit,
    bool AutomaticMiss);

/// <summary>Regras de D20 Test do SRD 5.2.1.</summary>
public static class D20
{
    /// <summary>
    /// Combina fontes de vantagem e desvantagem: uma única vantagem basta,
    /// e vantagem + desvantagem se cancelam, independentemente da quantidade.
    /// </summary>
    public static RollMode Combine(int advantageSources, int disadvantageSources)
    {
        if (advantageSources < 0) throw new ArgumentOutOfRangeException(nameof(advantageSources));
        if (disadvantageSources < 0) throw new ArgumentOutOfRangeException(nameof(disadvantageSources));
        bool hasAdvantage = advantageSources > 0;
        bool hasDisadvantage = disadvantageSources > 0;
        if (hasAdvantage == hasDisadvantage) return RollMode.Normal;
        return hasAdvantage ? RollMode.Advantage : RollMode.Disadvantage;
    }

    /// <summary>Rola o d20 base respeitando o modo; retorna os dados rolados e o resultado natural usado.</summary>
    public static (int[] Dice, int Natural) RollNatural(IRandomSource random, RollMode mode)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (mode == RollMode.Normal)
        {
            int one = random.Die(20);
            return (new[] { one }, one);
        }

        int a = random.Die(20);
        int b = random.Die(20);
        int natural = mode == RollMode.Advantage ? Math.Max(a, b) : Math.Min(a, b);
        return (new[] { a, b }, natural);
    }

    /// <summary>Executa um D20 Test completo contra um número-alvo (CD ou Classe de Armadura).</summary>
    public static D20Result Test(
        IRandomSource random,
        int modifier,
        int targetNumber,
        RollMode mode = RollMode.Normal,
        D20Kind kind = D20Kind.Check)
    {
        var (dice, natural) = RollNatural(random, mode);
        int total = checked(natural + modifier);

        bool critical = kind == D20Kind.Attack && natural == 20;
        bool autoMiss = kind == D20Kind.Attack && natural == 1;
        bool success = critical || (!autoMiss && total >= targetNumber);
        return new D20Result(dice, natural, mode, modifier, total, targetNumber, success, critical, autoMiss);
    }

    /// <summary>Iniciativa é um teste de Destreza: d20 + modificador de Destreza (e outros bônus).</summary>
    public static D20Result RollInitiative(IRandomSource random, int initiativeModifier, RollMode mode = RollMode.Normal)
    {
        var (dice, natural) = RollNatural(random, mode);
        int total = checked(natural + initiativeModifier);
        return new D20Result(dice, natural, mode, initiativeModifier, total, 0, true, false, false);
    }
}
