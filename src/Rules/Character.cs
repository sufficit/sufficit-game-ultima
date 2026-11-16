using System;
using System.Collections.Generic;

namespace Ultima.Rules;

// Regras baseadas no System Reference Document 5.2.1 (D&D 2024), licença CC-BY-4.0.
// Fonte: https://www.dndbeyond.com/srd

public enum Ability { Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma }

public enum CharacterClass
{
    Barbarian, Bard, Cleric, Druid, Fighter, Monk,
    Paladin, Ranger, Rogue, Sorcerer, Warlock, Wizard
}

public sealed record ClassInfo(
    CharacterClass Class,
    int HitDie,
    Ability[] PrimaryAbilities,
    Ability[] SavingThrows);

public static class ClassTable
{
    private static readonly Dictionary<CharacterClass, ClassInfo> Table = new()
    {
        [CharacterClass.Barbarian] = new(CharacterClass.Barbarian, 12, new[] { Ability.Strength }, new[] { Ability.Strength, Ability.Constitution }),
        [CharacterClass.Bard] = new(CharacterClass.Bard, 8, new[] { Ability.Charisma }, new[] { Ability.Dexterity, Ability.Charisma }),
        [CharacterClass.Cleric] = new(CharacterClass.Cleric, 8, new[] { Ability.Wisdom }, new[] { Ability.Wisdom, Ability.Charisma }),
        [CharacterClass.Druid] = new(CharacterClass.Druid, 8, new[] { Ability.Wisdom }, new[] { Ability.Intelligence, Ability.Wisdom }),
        [CharacterClass.Fighter] = new(CharacterClass.Fighter, 10, new[] { Ability.Strength, Ability.Dexterity }, new[] { Ability.Strength, Ability.Constitution }),
        [CharacterClass.Monk] = new(CharacterClass.Monk, 8, new[] { Ability.Dexterity, Ability.Wisdom }, new[] { Ability.Strength, Ability.Dexterity }),
        [CharacterClass.Paladin] = new(CharacterClass.Paladin, 10, new[] { Ability.Strength, Ability.Charisma }, new[] { Ability.Wisdom, Ability.Charisma }),
        [CharacterClass.Ranger] = new(CharacterClass.Ranger, 10, new[] { Ability.Dexterity, Ability.Wisdom }, new[] { Ability.Strength, Ability.Dexterity }),
        [CharacterClass.Rogue] = new(CharacterClass.Rogue, 8, new[] { Ability.Dexterity }, new[] { Ability.Dexterity, Ability.Intelligence }),
        [CharacterClass.Sorcerer] = new(CharacterClass.Sorcerer, 6, new[] { Ability.Charisma }, new[] { Ability.Constitution, Ability.Charisma }),
        [CharacterClass.Warlock] = new(CharacterClass.Warlock, 8, new[] { Ability.Charisma }, new[] { Ability.Wisdom, Ability.Charisma }),
        [CharacterClass.Wizard] = new(CharacterClass.Wizard, 6, new[] { Ability.Intelligence }, new[] { Ability.Intelligence, Ability.Wisdom }),
    };

    public static ClassInfo Get(CharacterClass c) => Table[c];
}

public static class Progression
{
    public const int MaxLevel = 20;

    /// <summary>XP mínimo para cada nível (índice 0 = nível 1).</summary>
    private static readonly int[] XpThresholds =
    {
        0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000,
        85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000, 305000, 355000
    };

    public static int XpForLevel(int level)
    {
        CheckLevel(level);
        return XpThresholds[level - 1];
    }

    public static int LevelForXp(int xp)
    {
        if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
        int level = 1;
        for (int i = 1; i < XpThresholds.Length; i++)
            if (xp >= XpThresholds[i]) level = i + 1;
        return level;
    }

    /// <summary>Bônus de proficiência: +2 nos níveis 1-4, +3 em 5-8, +4 em 9-12, +5 em 13-16, +6 em 17-20.</summary>
    public static int ProficiencyBonus(int level)
    {
        CheckLevel(level);
        return 2 + (level - 1) / 4;
    }

    /// <summary>Modificador de atributo: (valor - 10) / 2, arredondado para baixo.</summary>
    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    private static void CheckLevel(int level)
    {
        if (level < 1 || level > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level), "O nível deve estar entre 1 e 20.");
    }
}

public static class AbilityGeneration
{
    public static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

    public const int PointBuyBudget = 27;

    public static int PointCost(int score) => score switch
    {
        8 => 0, 9 => 1, 10 => 2, 11 => 3, 12 => 4, 13 => 5, 14 => 7, 15 => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(score), "Compra de pontos aceita valores de 8 a 15.")
    };

    public static bool IsValidPointBuy(IReadOnlyCollection<int> scores)
    {
        if (scores.Count != 6) return false;
        int total = 0;
        foreach (int score in scores)
        {
            if (score < 8 || score > 15) return false;
            total += PointCost(score);
        }
        return total <= PointBuyBudget;
    }

    /// <summary>Geração aleatória: rola 4d6 e soma os três maiores, seis vezes.</summary>
    public static int[] RollRandom(IRandomSource random)
    {
        var scores = new int[6];
        for (int i = 0; i < scores.Length; i++) scores[i] = Dice.Roll("4d6kh3", random).Total;
        return scores;
    }
}

public sealed class Character
{
    private readonly int[] _scores = { 10, 10, 10, 10, 10, 10 };

    public string Name { get; set; } = "Herói";
    public CharacterClass Class { get; }
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; }
    public int MaxHitPoints { get; private set; }
    public int HitPoints { get; private set; }
    public int DeathSaveSuccesses { get; private set; }
    public int DeathSaveFailures { get; private set; }
    public bool Stable { get; private set; }
    public bool Dead { get; private set; }

    public Character(string name, CharacterClass characterClass, IReadOnlyList<int> abilityScores)
    {
        if (abilityScores is null || abilityScores.Count != 6)
            throw new ArgumentException("São necessários seis valores de atributo.", nameof(abilityScores));
        for (int i = 0; i < 6; i++)
        {
            if (abilityScores[i] is < 1 or > 30)
                throw new ArgumentOutOfRangeException(nameof(abilityScores), "Atributos ficam entre 1 e 30.");
            _scores[i] = abilityScores[i];
        }
        Name = name;
        Class = characterClass;
        // Nível 1: valor máximo do dado de vida + modificador de Constituição.
        MaxHitPoints = Math.Max(1, ClassTable.Get(characterClass).HitDie + ConMod);
        HitPoints = MaxHitPoints;
    }

    private int ConMod => Progression.Modifier(Score(Ability.Constitution));

    public int Score(Ability a) => _scores[(int)a];
    public int Mod(Ability a) => Progression.Modifier(Score(a));
    public int ProficiencyBonus => Progression.ProficiencyBonus(Level);
    public bool IsProficientInSave(Ability a) => Array.IndexOf(ClassTable.Get(Class).SavingThrows, a) >= 0;
    public int SaveModifier(Ability a) => Mod(a) + (IsProficientInSave(a) ? ProficiencyBonus : 0);

    /// <summary>CA sem armadura: 10 + modificador de Destreza.</summary>
    public int UnarmoredArmorClass => 10 + Mod(Ability.Dexterity);

    public int InitiativeModifier => Mod(Ability.Dexterity);

    public D20Result RollInitiative(IRandomSource random, RollMode mode = RollMode.Normal)
        => D20.RollInitiative(random, InitiativeModifier, mode);

    public D20Result RollSave(IRandomSource random, Ability a, int dc, RollMode mode = RollMode.Normal)
        => D20.Test(random, SaveModifier(a), dc, mode, D20Kind.Check);

    public D20Result RollAttack(IRandomSource random, Ability a, bool proficient, int armorClass, RollMode mode = RollMode.Normal)
        => D20.Test(random, Mod(a) + (proficient ? ProficiencyBonus : 0), armorClass, mode, D20Kind.Attack);

    /// <summary>Ganha XP e sobe de nível quando o limite é atingido; retorna quantos níveis subiu.</summary>
    public int GainExperience(int xp, IRandomSource? random = null, bool useAverageHp = true)
    {
        if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
        Experience += xp;
        int gained = 0;
        while (Level < Progression.MaxLevel && Experience >= Progression.XpForLevel(Level + 1))
        {
            LevelUp(random, useAverageHp);
            gained++;
        }
        return gained;
    }

    private void LevelUp(IRandomSource? random, bool useAverageHp)
    {
        int die = ClassTable.Get(Class).HitDie;
        Level++;
        int hp;
        if (useAverageHp || random is null) hp = die / 2 + 1;
        else hp = random.Die(die);
        hp = Math.Max(1, hp + ConMod);
        MaxHitPoints += hp;
        HitPoints += hp;
    }

    public void Heal(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Dead) return;
        if (HitPoints == 0 && amount > 0) ResetDeathSaves();
        HitPoints = Math.Min(MaxHitPoints, HitPoints + amount);
    }

    /// <summary>Aplica dano. A 0 PV o dano causa uma falha de morte (duas se crítico); dano restante >= PV máximo mata na hora.</summary>
    public void TakeDamage(int amount, bool critical = false)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Dead || amount == 0) return;

        if (HitPoints == 0)
        {
            // SRD 5.2.1: dano igual ou maior que o PV máximo mata; senão causa falha(s) de morte.
            if (amount >= MaxHitPoints) { Dead = true; return; }
            Stable = false;
            AddDeathFailures(critical ? 2 : 1);
            return;
        }

        int overflow = amount - HitPoints;
        HitPoints = Math.Max(0, HitPoints - amount);
        if (HitPoints == 0)
        {
            if (overflow >= MaxHitPoints) Dead = true;
            else { Stable = false; ResetDeathSaves(); }
        }
    }

    /// <summary>Teste de morte: CD 10, sem modificadores. 20 natural: volta com 1 PV; 1 natural: duas falhas.</summary>
    public D20Result RollDeathSave(IRandomSource random, RollMode mode = RollMode.Normal)
    {
        if (HitPoints != 0 || Dead || Stable)
            throw new InvalidOperationException("Testes de morte só ocorrem com 0 PV, vivo e instável.");

        var result = D20.Test(random, 0, 10, mode, D20Kind.Check);
        if (result.Natural == 20)
        {
            HitPoints = 1;
            ResetDeathSaves();
        }
        else if (result.Natural == 1) AddDeathFailures(2);
        else if (result.Success) AddDeathSuccess();
        else AddDeathFailures(1);
        return result;
    }

    /// <summary>Um personagem Estável que não foi curado recupera 1 PV após 1d4 horas (SRD 5.2.1).</summary>
    public int RollStableRecoveryHours(IRandomSource random) => random.Die(4);

    public void RecoverFromStable()
    {
        if (!Stable || Dead) throw new InvalidOperationException("Apenas um personagem Estável recupera PV dessa forma.");
        HitPoints = 1;
        Stable = false;
    }

    private void AddDeathSuccess()
    {
        if (++DeathSaveSuccesses >= 3) { Stable = true; DeathSaveSuccesses = 0; DeathSaveFailures = 0; }
    }

    private void AddDeathFailures(int n)
    {
        DeathSaveFailures += n;
        if (DeathSaveFailures >= 3) { Dead = true; DeathSaveFailures = 3; }
    }

    private void ResetDeathSaves()
    {
        DeathSaveSuccesses = 0;
        DeathSaveFailures = 0;
        Stable = false;
    }
}
