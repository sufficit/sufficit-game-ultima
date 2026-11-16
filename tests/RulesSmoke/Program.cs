using Ultima.Rules;

int failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}");
    if (!ok) failures++;
}

bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; }
    catch (T) { return true; }
}

// RNG determinístico
var a = new Rng(12345);
var b = new Rng(12345);
bool same = true;
for (int i = 0; i < 1000; i++) same &= a.NextUInt64() == b.NextUInt64();
Check("mesma semente => mesma sequência", same);
Check("sementes diferentes divergem", new Rng(1).NextUInt64() != new Rng(2).NextUInt64());
var rng = new Rng(7);
var counts = new int[21];
for (int i = 0; i < 200_000; i++) counts[rng.Die(20)]++;
Check("d20 dentro do intervalo", counts[0] == 0);
Check("d20 distribuição plausível", counts.Skip(1).All(c => c > 9000 && c < 11000));

// Dados e parser
Check("parse 2d6+3", Dice.Parse("2d6+3") is { Count: 2, Sides: 6, Modifier: 3 });
Check("parse d20", Dice.Parse("d20").Normalized == "1d20+0");
Check("parse d%", Dice.Parse("d%").Sides == 100);
Check("4d6kh3 mantém 3 maiores", Dice.Roll("4d6kh3", new Scripted(1, 6, 5, 4)).Total == 15);
Check("4d6kl1 mantém o menor", Dice.Roll("4d6kl1", new Scripted(3, 6, 2, 4)).Total == 2);
Check("modificador negativo", Dice.Roll("1d8-2", new Scripted(5)).Total == 3);
Check("d7 inválido", Throws<FormatException>(() => Dice.Parse("1d7")));
Check("notação inválida", Throws<FormatException>(() => Dice.Parse("abc")));
Check("kh inválido", Throws<FormatException>(() => Dice.Parse("2d6kh3")));
Check("crítico duplica dados, não modificador", Dice.RollDamage("1d8+3", new Scripted(4, 5), true).Total == 12);

// Progressão e atributos
Check("modificadores com arredondamento para baixo", Progression.Modifier(10) == 0 && Progression.Modifier(8) == -1 && Progression.Modifier(15) == 2 && Progression.Modifier(20) == 5 && Progression.Modifier(9) == -1);
int[] pb = { 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6 };
Check("bônus de proficiência níveis 1-20", Enumerable.Range(1, 20).All(l => Progression.ProficiencyBonus(l) == pb[l - 1]));
Check("XP nível 2/5/20", Progression.XpForLevel(2) == 300 && Progression.XpForLevel(5) == 6500 && Progression.XpForLevel(20) == 355000);
Check("nível determinado por XP", Progression.LevelForXp(299) == 1 && Progression.LevelForXp(300) == 2 && Progression.LevelForXp(999999) == 20);
Check("point buy custo 14/15", AbilityGeneration.PointCost(14) == 7 && AbilityGeneration.PointCost(15) == 9);
Check("array padrão custa 27 pontos", AbilityGeneration.StandardArray.Sum(AbilityGeneration.PointCost) == 27);
Check("point buy válido", AbilityGeneration.IsValidPointBuy(new[] { 15, 15, 15, 8, 8, 8 }));
Check("point buy excedido", !AbilityGeneration.IsValidPointBuy(new[] { 15, 15, 15, 15, 8, 8 }));
var rolled = AbilityGeneration.RollRandom(new Rng(99));
Check("4d6kh3 gera seis valores entre 3 e 18", rolled.Length == 6 && rolled.All(v => v is >= 3 and <= 18));

// d20 Test
Check("vantagem e desvantagem se cancelam", D20.Combine(2, 1) == RollMode.Normal && D20.Combine(0, 0) == RollMode.Normal);
Check("vantagem não empilha", D20.Combine(3, 0) == RollMode.Advantage);
Check("vantagem escolhe maior", D20.RollNatural(new Scripted(4, 17), RollMode.Advantage).Natural == 17);
Check("desvantagem escolhe menor", D20.RollNatural(new Scripted(4, 17), RollMode.Disadvantage).Natural == 4);
Check("20 natural ataque acerta e é crítico", D20.Test(new Scripted(20), -10, 30, RollMode.Normal, D20Kind.Attack) is { Success: true, CriticalHit: true });
Check("1 natural ataque erra", D20.Test(new Scripted(1), 20, 5, RollMode.Normal, D20Kind.Attack) is { Success: false, AutomaticMiss: true });
Check("20 natural não passa automaticamente teste de atributo", !D20.Test(new Scripted(20), 0, 30).Success);
Check("teste de atributo soma modificador", D20.Test(new Scripted(10), 5, 15).Success);

// Personagens e progressão
var fighter = new Character("Teste", CharacterClass.Fighter, new[] { 15, 14, 13, 12, 13, 8 });
Check("guerreiro nível 1: d10 máximo + Con (+1) = 11 PV", fighter.MaxHitPoints == 11);
Check("CA sem armadura = 10 + Des", fighter.UnarmoredArmorClass == 12);
Check("salvaguardas de classe", fighter.IsProficientInSave(Ability.Strength) && fighter.IsProficientInSave(Ability.Constitution) && !fighter.IsProficientInSave(Ability.Wisdom));
Check("salvaguarda Força soma proficiência", fighter.SaveModifier(Ability.Strength) == 4);
Check("iniciativa usa mod Des", fighter.InitiativeModifier == 2);
Check("300 XP sobe ao nível 2 com +6+Con PV", fighter.GainExperience(300) == 1 && fighter.Level == 2 && fighter.MaxHitPoints == 18);
var wizard = new Character("Mago", CharacterClass.Wizard, new[] { 8, 14, 8, 15, 12, 10 });
Check("mago com Con 8 começa com 5 PV", wizard.MaxHitPoints == 5);

// Testes de morte
var down = new Character("Caído", CharacterClass.Fighter, new[] { 10, 10, 10, 10, 10, 10 });
down.TakeDamage(10);
Check("dano leva a 0 PV sem matar", down.HitPoints == 0 && !down.Dead);
down.RollDeathSave(new Scripted(1));
Check("1 natural gera duas falhas", down.DeathSaveFailures == 2);
down.RollDeathSave(new Scripted(9));
Check("terceira falha mata", down.Dead);

var stable = new Character("Estável", CharacterClass.Fighter, new[] { 10, 10, 10, 10, 10, 10 });
stable.TakeDamage(10);
stable.RollDeathSave(new Scripted(10));
stable.RollDeathSave(new Scripted(15));
stable.RollDeathSave(new Scripted(11));
Check("3 sucessos estabilizam sem recuperar PV e zeram contadores", stable.Stable && stable.DeathSaveSuccesses == 0 && stable.HitPoints == 0);

var lucky = new Character("Sortudo", CharacterClass.Fighter, new[] { 10, 10, 10, 10, 10, 10 });
lucky.TakeDamage(10);
lucky.RollDeathSave(new Scripted(20));
Check("20 natural recupera 1 PV", lucky.HitPoints == 1 && lucky.DeathSaveFailures == 0);

var criticalDamage = new Character("Crítico", CharacterClass.Fighter, new[] { 10, 10, 10, 10, 10, 10 });
criticalDamage.TakeDamage(10);
criticalDamage.TakeDamage(3, critical: true);
Check("crítico contra personagem a 0 PV gera duas falhas", criticalDamage.DeathSaveFailures == 2);

var massive = new Character("Massivo", CharacterClass.Wizard, new[] { 10, 10, 10, 10, 10, 10 });
massive.TakeDamage(12);
Check("dano restante igual ao PV máximo mata", massive.Dead);

var instant = new Character("Instantâneo", CharacterClass.Fighter, new[] { 10, 10, 10, 10, 10, 10 });
instant.TakeDamage(10);
instant.TakeDamage(10);
Check("dano >= PV máximo com 0 PV mata na hora", instant.Dead);

stable.RecoverFromStable();
Check("Estável não curado recupera 1 PV", stable.HitPoints == 1 && !stable.Stable);
Check("1d4 horas de espera", Enumerable.Range(0, 50).Select(i => new Rng((ulong)i)).All(r2 => stable.RollStableRecoveryHours(r2) is >= 1 and <= 4));

Console.WriteLine(failures == 0 ? "\nTODOS OS TESTES PASSARAM" : $"\n{failures} FALHA(S)");
return failures == 0 ? 0 : 1;

sealed class Scripted : IRandomSource
{
    private readonly Queue<int> _values;
    public Scripted(params int[] values) => _values = new Queue<int>(values);
    public int Die(int sides) => _values.Dequeue();
}
