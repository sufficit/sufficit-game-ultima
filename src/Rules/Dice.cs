using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Ultima.Rules;

/// <summary>Fonte de dados; pode ser substituída por uma sequência controlada nos testes.</summary>
public interface IRandomSource
{
    int Die(int sides);
}

/// <summary>
/// Gerador pseudoaleatório determinístico xoshiro256**, semeado por SplitMix64.
/// Uma mesma semente reproduz a mesma sequência. Não é um gerador criptográfico.
/// </summary>
public sealed class Rng : IRandomSource
{
    private ulong _s0, _s1, _s2, _s3;

    public ulong Seed { get; }

    public Rng(ulong seed)
    {
        Seed = seed;
        ulong state = seed;
        _s0 = SplitMix64(ref state);
        _s1 = SplitMix64(ref state);
        _s2 = SplitMix64(ref state);
        _s3 = SplitMix64(ref state);
        if ((_s0 | _s1 | _s2 | _s3) == 0UL)
            _s0 = 0x9E3779B97F4A7C15UL;
    }

    /// <summary>Cria uma semente a partir de entropia criptográfica do sistema.</summary>
    public static Rng FromEntropy()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return new Rng(System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes));
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Próximo inteiro de 64 bits.</summary>
    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Inteiro uniforme no intervalo [minInclusive, maxExclusive), sem viés de módulo.</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "O intervalo precisa ter ao menos um valor.");

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong threshold = unchecked(0UL - range) % range;
        ulong value;
        do { value = NextUInt64(); } while (value < threshold);
        return checked((int)(minInclusive + (long)(value % range)));
    }

    public int Next(int maxExclusive) => Next(0, maxExclusive);

    /// <summary>Rola um dado uniforme com faces numeradas de 1 até sides.</summary>
    public int Die(int sides)
    {
        if (sides < 1 || sides == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sides));
        return Next(1, sides + 1);
    }

    public int[] Roll(int count, int sides)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = Die(sides);
        return result;
    }
}

public enum DiceKeep
{
    None,
    Highest,
    Lowest
}

public readonly record struct DiceNotation(
    int Count,
    int Sides,
    DiceKeep Keep,
    int KeepCount,
    int Modifier,
    string Normalized);

/// <summary>Resultado auditável de uma rolagem, mantendo os dados individuais.</summary>
public sealed record DiceRoll(
    string Notation,
    int[] Rolled,
    int[] Kept,
    int Modifier,
    int Total)
{
    public override string ToString() => $"{Notation} = [{string.Join(',', Rolled)}] => {Total}";
}

/// <summary>
/// Parser e resolvedor de dados: d20, 2d6+3, 3d8-1, d%, 4d6kh3.
/// O formato aceita um conjunto de dados comuns e até 64 dados por expressão.
/// </summary>
public static class Dice
{
    private static readonly Regex Pattern = new(
        @"^\s*(?<count>\d*)\s*d\s*(?<sides>\d+|%)\s*(?<keep>kh|kl)?\s*(?<keepCount>\d+)?\s*(?<modifier>[+-]\s*\d+)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValidSides(int sides) => sides is 2 or 3 or 4 or 6 or 8 or 10 or 12 or 20 or 100;

    public static DiceNotation Parse(string notation)
    {
        ArgumentNullException.ThrowIfNull(notation);
        Match match = Pattern.Match(notation);
        if (!match.Success)
            throw new FormatException($"Notação de dados inválida: '{notation}'.");

        int count = match.Groups["count"].Length == 0
            ? 1
            : int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
        if (count is < 1 or > 64)
            throw new FormatException("A expressão precisa rolar entre 1 e 64 dados.");

        string sideToken = match.Groups["sides"].Value;
        int sides = sideToken == "%"
            ? 100
            : int.Parse(sideToken, CultureInfo.InvariantCulture);
        if (!IsValidSides(sides))
            throw new FormatException($"d{sides} não faz parte do conjunto de dados aceito.");

        DiceKeep keep = match.Groups["keep"].Value.ToLowerInvariant() switch
        {
            "kh" => DiceKeep.Highest,
            "kl" => DiceKeep.Lowest,
            _ => DiceKeep.None
        };
        int keepCount = keep == DiceKeep.None
            ? count
            : match.Groups["keepCount"].Length == 0
                ? 1
                : int.Parse(match.Groups["keepCount"].Value, CultureInfo.InvariantCulture);
        if (keepCount < 1 || keepCount > count)
            throw new FormatException("A quantidade de dados mantidos deve estar entre 1 e a quantidade rolada.");

        string modifierToken = match.Groups["modifier"].Value.Replace(" ", "", StringComparison.Ordinal);
        int modifier = modifierToken.Length == 0
            ? 0
            : int.Parse(modifierToken, CultureInfo.InvariantCulture);
        string keepText = keep switch
        {
            DiceKeep.Highest => $"kh{keepCount}",
            DiceKeep.Lowest => $"kl{keepCount}",
            _ => string.Empty
        };
        string normalized = $"{count}d{sides}{keepText}{(modifier >= 0 ? "+" : string.Empty)}{modifier}";
        return new DiceNotation(count, sides, keep, keepCount, modifier, normalized);
    }

    public static DiceRoll Roll(string notation, IRandomSource random) => Roll(Parse(notation), random);

    public static DiceRoll Roll(DiceNotation notation, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int[] rolled = new int[notation.Count];
        for (int i = 0; i < rolled.Length; i++) rolled[i] = random.Die(notation.Sides);

        int[] kept;
        if (notation.Keep == DiceKeep.None)
        {
            kept = (int[])rolled.Clone();
        }
        else
        {
            int[] ordered = (int[])rolled.Clone();
            Array.Sort(ordered);
            if (notation.Keep == DiceKeep.Highest) Array.Reverse(ordered);
            kept = ordered[..notation.KeepCount];
        }

        int sum = 0;
        foreach (int value in kept) sum = checked(sum + value);
        int total = checked(sum + notation.Modifier);
        return new DiceRoll(notation.Normalized, rolled, kept, notation.Modifier, total);
    }

    /// <summary>Rola dano de arma; em acerto crítico duplica os dados, não o modificador plano.</summary>
    public static DiceRoll RollDamage(string notation, IRandomSource random, bool criticalHit)
    {
        DiceNotation parsed = Parse(notation);
        if (!criticalHit) return Roll(parsed, random);
        if (parsed.Keep != DiceKeep.None)
            throw new FormatException("Dano crítico não aceita kh/kl na notação de dano.");
        if (parsed.Count > 32)
            throw new FormatException("Dano crítico excederia o limite de 64 dados.");

        var critical = parsed with
        {
            Count = parsed.Count * 2,
            KeepCount = parsed.Count * 2,
            Normalized = $"{parsed.Count * 2}d{parsed.Sides}{(parsed.Modifier >= 0 ? "+" : string.Empty)}{parsed.Modifier}"
        };
        return Roll(critical, random);
    }
}
