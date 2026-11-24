using Ultima.Npc;
using Ultima.Rules;
using Ultima.Terrain;
using System.Runtime.InteropServices;

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

// IA de NPCs: decisões sobre snapshot com budget por tick (docs/PLAN-NPC-AI-DECISIONS.md)
WorldSnapshot Snap(bool near = true, bool open = false, bool ai = false, bool nearWanderer = false) => new(0, -12, near, nearWanderer, open, ai, 1000);
var npc = new NpcScheduler();
npc.Submit(new PlayerInteracted(1000));
var open = npc.Tick(Snap()).Single();
Check("interação perto abre diálogo na linha 0", open.Commands.Any(c => c is OpenDialog) && open.View is { CurrentLine: 0, Thinking: false });
Check("falas de abertura padrão têm 5 linhas", open.View!.Lines.Count == 5);

npc.Submit(new PlayerInteracted(1100));
var adv = npc.Tick(Snap(open: true)).Single();
Check("avanço vai para a linha 1", adv.Commands.Any(c => c is ShowLine { LineIndex: 1 }) && adv.View is { CurrentLine: 1 });

for (var i = 0; i < 3; i++) { npc.Submit(new PlayerInteracted(1200 + i)); npc.Tick(Snap(open: true)); }
npc.Submit(new PlayerInteracted(1300));
var close = npc.Tick(Snap(open: true)).Single();
Check("avanço na última linha encerra o diálogo", close.Commands.Any(c => c is CloseDialog) && close.View is { CurrentLine: -1 });

npc.Submit(new PlayerInteracted(1400));
npc.Tick(Snap(open: false, near: false));
Check("interação longe da Guardiã não abre diálogo", npc.PendingEvents == 0);

npc.Submit(new PlayerInteracted(1500));
npc.Tick(Snap(open: false));
npc.Submit(new PlayerSpoke(1600, "Olá!"));
var think = npc.Tick(Snap(open: true)).Single();
Check("fala do jogador vira reticências pendentes", think.Commands.Any(c => c is BeginThinking) && think.View is { Thinking: true } && think.View.Lines[^1].Text == "…" && think.View.Lines[^2] is { Speaker: "Você", Text: "Olá!" });

npc.Submit(new PlayerSpoke(1700, "outra mensagem antes da resposta"));
var flood = npc.Tick(Snap(open: true));
Check("fala durante o pensamento é ignorada (anti-inundação no cérebro)", flood.Count == 0);

npc.Submit(new ReplyArrived(1800, "Resposta da GPU.", "GPU do jogador"));
var reply = npc.Tick(Snap(open: true, ai: true)).Single();
Check("resposta do produtor caro substitui as reticências", reply.Commands.Any(c => c is SetReply { Text: "Resposta da GPU." }) && reply.View is { Thinking: false, ReplySource: "GPU do jogador" });

npc.Submit(new IntroLinesChanged(1900, "Guardiã Remota", ["Fala remota 1", "Fala remota 2"]));
var intro = npc.Tick(Snap(open: true)).Single();
Check("conteúdo remoto troca as falas de abertura", intro.View!.Lines.Count == 2 && intro.View.Lines[0] is { Speaker: "Guardiã Remota", Text: "Fala remota 1" });

// Segundo NPC: o Andarilho prova que o desenho escala sem mudar o loop
var two = new NpcScheduler();
Check("dois cérebros registrados no agendador", two.Npcs.Count == 2 && two.Npcs.Contains("guardia") && two.Npcs.Contains("andarilho"));

two.Submit(new PlayerInteracted(1000));
var wandererOpen = two.Tick(Snap(near: false, nearWanderer: true)).Single();
Check("perto do Andarilho (longe da Guardiã) abre o diálogo dele", wandererOpen.Commands.Any(c => c is OpenDialog) && wandererOpen.View is { Speaker: "Andarilho", CurrentLine: 0 });
Check("falas do Andarilho têm 3 linhas", wandererOpen.View!.Lines.Count == 3 && wandererOpen.View.Lines[0].Text.Contains("montanhas", StringComparison.Ordinal));

two.Submit(new PlayerSpoke(1100, "Para onde leva essa trilha?"));
var wThink = two.Tick(Snap(near: false, nearWanderer: true, open: true)).Single();
two.Submit(new ReplyArrived(1200, "Até a neve.", "servidor · regras"));
var wReply = two.Tick(Snap(near: false, nearWanderer: true, open: true)).Single();
Check("fala e resposta do Andarilho vão para o diálogo aberto dele", wThink.View is { Thinking: true } && wReply.View is { Speaker: "Andarilho", Thinking: false } && wReply.Commands.Any(c => c is SetReply { Text: "Até a neve." }));

var both = new NpcScheduler();
both.Submit(new PlayerInteracted(1000));
var bothOpen = both.Tick(Snap(near: true, nearWanderer: true)).Single();
Check("perto dos dois, a Guardiã tem prioridade", bothOpen.View is { Speaker: "Guardiã do Jardim" });

var remote = new NpcScheduler();
remote.Submit(new PlayerInteracted(1000));
remote.Tick(Snap(near: false, nearWanderer: true));   // abre o Andarilho
remote.Submit(new IntroLinesChanged(1100, "Guardiã Remota", ["nova fala"]));
var introRouted = remote.Tick(Snap(near: false, nearWanderer: true, open: true));
Check("conteúdo remoto segue para a Guardiã mesmo com o Andarilho aberto", introRouted.Count == 0);   // não troca as falas do diálogo do Andarilho

// Budget por tick: rajada de eventos não passa do orçamento; o resto retoma no tick seguinte
var busy = new NpcScheduler();
for (var i = 0; i < 10; i++) { busy.Submit(new PlayerInteracted(2000 + i)); }
var first = busy.Tick(Snap(open: false));
Check("tick processa no máximo MaxEventsPerTick eventos", NpcScheduler.MaxEventsPerTick == 4 && busy.PendingEvents == 6);
Check("cada evento processado produziu decisão", first.Count == 4);
var rest = 0;
while (busy.PendingEvents > 0) { rest += busy.Tick(Snap(open: true)).Count; }
Check("fila restante drena nos ticks seguintes", rest == 6);

for (var i = 0; i < NpcScheduler.MaxQueuedEvents + 10; i++) { busy.Submit(new PlayerInteracted(3000 + i)); }
Check("transbordo da fila descarta o mais antigo sem estourar", busy.PendingEvents == NpcScheduler.MaxQueuedEvents && busy.DroppedEvents == 10);

var surface = new TerrainSurface();
Check("terreno mantém a altura da clareira inicial", surface.Height(0, 0) == 0.05);
var baseGeometry = TerrainMesh.Build(surface, 0, 0);
Check("terreno base mantém a grade de 64 células",
    MemoryMarshal.Cast<byte, int>(baseGeometry.AsSpan())[0] == 4225);
surface.AddDig(0, 0);
surface.AddDig(0, 0);
Check("escavação acumula duas camadas", Math.Abs(surface.Height(0, 0) + 0.91) < 1e-10);
var refinedGeometry = TerrainMesh.Build(surface, 0, 0);
Check("a malha refina apenas perto da escavação", MemoryMarshal.Cast<byte, int>(refinedGeometry.AsSpan())[0] > 4225);
surface.SetEdits([0, 0, 0, 2, 1]);
Check("escultura preserva as marcas da mochila", Math.Abs(surface.Height(0, 0) - 0.09) < 1e-10);

var heightfield = new TerrainSurface();
var volumes = new TerrainVolumes(heightfield);
volumes.Set([2, 3, 13, -2, 2, 16, -2, 0, 20, -2.1, 3]);
var groundY = heightfield.Height(16, 0);
var lowerFloor = volumes.Floor(16, 0, -3.27, 0.4);
Check("corredor mantém piso subterrâneo e superfície na mesma coordenada", lowerFloor < -3 &&
    Math.Abs(volumes.Floor(16, 0, groundY, 0.4) - groundY) < 0.003);
Check("campo do corredor liberta a passagem e preserva o teto",
    !volumes.Blocks(16, -2, 0, 0.1) && volumes.Blocks(16, 0.1, 0, 0)
    && volumes.Blocks(16, -2, 5, 0));
var leftTunnel = TerrainVolumeMesh.Build(heightfield, volumes, volumes.InChunk(0, 0), 0, 0);
var rightTunnel = TerrainVolumeMesh.Build(heightfield, volumes, volumes.InChunk(1, 0), 1, 0);
Check("corredor cruza chunks usando o mesmo contorno voxelizado",
    TunnelEdge(leftTunnel).SetEquals(TunnelEdge(rightTunnel)) && TunnelEdge(leftTunnel).Count > 10);
Check("mesher mantém faces subterrâneas", MemoryMarshal.Cast<byte, int>(leftTunnel.AsSpan())[1] > 18000);

static HashSet<(float Y, float Z)> TunnelEdge(byte[] mesh)
{
    var count = MemoryMarshal.Cast<byte, int>(mesh.AsSpan())[0];
    var positions = MemoryMarshal.Cast<byte, float>(mesh.AsSpan())[3..];
    var edge = new HashSet<(float Y, float Z)>();
    for (var vertex = 0; vertex < count; vertex++)
    {
        var p = vertex * 3;
        if (positions[p] == 16 && positions[p + 1] < -1) edge.Add((positions[p + 1], positions[p + 2]));
    }
    return edge;
}

var marks = new double[] { 0.50, 0.62 };
Check("timeline emite impacto e áudio no cruzamento",
    Ultima.AnimationTimeline.Crossed(0.49, 0.63, false, marks).SequenceEqual(new[] { 0, 1 }));
Check("timeline mantém pausa e não repete uma marca de fronteira",
    Ultima.AnimationTimeline.Crossed(0.50, 0.50, false, marks).Length == 0 &&
    Ultima.AnimationTimeline.Crossed(0.50, 0.62, false, marks).SequenceEqual(new[] { 1 }));
Check("timeline recomeça o ciclo em ordem",
    Ultima.AnimationTimeline.Crossed(0.51, 1.51, true, marks).SequenceEqual(new[] { 1, 0 }));
Check("troca de ação descarta as marcas do clipe anterior",
    Ultima.AnimationTimeline.Crossed(0, 0.2, false, marks).Length == 0 &&
    Ultima.AnimationTimeline.Crossed(0.7, 1.8, false, marks).Length == 0);
var animated = new NpcScheduler();
animated.Submit(new AnimationMarked(3010, "hit", "collect", default));
Check("agendador entrega impacto mesmo fora de diálogo",
    animated.Tick(Snap()).Single().Commands.Single() is ResolveAnimationMark { Name: "hit", Payload: "collect" });

// Regressões da simulação compartilhada com o navegador.
var movement = new PlayerMovement(1.75, 0.41, 0.03);
movement.SetColliders([0, 2, 0.28, 0, 5, 0]);
var blocked = movement.Sweep(0, 0.03, 0, 0, 5, 0);
Check("varredura contínua para na frente do tronco", Math.Abs(blocked.Z - 1.469) < 0.0001);
var slide = movement.Sweep(-0.40, 0.03, 0, 0, 3, 0);
Check("colisão oblíqua conserva o deslizamento", slide.X < -0.55 && slide.Z > 1.7);
Check("proximidade usa a mesma geometria da colisão", movement.NearbyObstacle(0, 0.03, 1) == 0);
movement.SetColliders([0, 2, 0.28, 0, 0.30, 0]);
Check("objeto abaixo do joelho permite avanço", movement.Sweep(0, 0.03, 0, 0, 5, 0).Z == 5);
movement.SetColliders([]);
Check("colisor removido também some da proximidade", movement.NearbyObstacle(0, 0.03, 1) == -1);
var axes = PlayerMovement.Stick(0.8, -0.8);
Check("zona morta radial preserva diagonal", PlayerMovement.Stick(0.1, 0.1) == (0, 0)
    && Math.Abs(axes.X + axes.Y) < 1e-12
    && Math.Abs(double.Hypot(axes.X, axes.Y) - 1) < 1e-12);
Check("alcance de coleta considera os três eixos", PlayerPose.CanCollect(0, 2.1, 2.8)
    && !PlayerPose.CanCollect(0, 2.2, 2.8));

Span<double> legs = stackalloc double[7];
PlayerPose.SolveLegs(legs, 0, 0.03, 0.03, 0.03, 0.77, 0.364, 0.051,
    0.1, 0, 0.22, true, -0.1, 0, 0.04, true);
Check("desnível eleva quadril com limite e mantém as solas niveladas", legs[0] > 0.03 && legs[0] <= 0.12
    && Math.Abs(legs[1] + legs[2] + legs[3]) < 1e-12 && Math.Abs(legs[4] + legs[5] + legs[6]) < 1e-12);

var camera = new PlayerCamera();
Span<double> orbit = stackalloc double[8];
camera.Advance(orbit, -Math.PI / 2, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0);
Check("órbita conserva o raio e o sentido inicial",
    Math.Abs(double.Hypot(double.Hypot(orbit[3], orbit[4]), orbit[5]) - 7.5) < 1e-12 && orbit[5] < 0 && orbit[7] == 1);
camera.Advance(orbit, 0, 1.12, 1.25, 40, 0.04, true, 1, 1, 20, -200, -12000);
Check("câmera congelada mantém arrasto, limita zoom e amortece retorno",
    orbit[0] == -0.20 && orbit[1] == 0.18 && orbit[2] == 2.4 && orbit[6] > 1.25 && orbit[6] < 7.5 && orbit[7] == 0);
camera.Reset();
camera.Advance(orbit, 0, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0);
Check("reiniciar restaura o zoom da câmera", orbit[2] == 7.5 && orbit[6] == 7.5);

var pausedAction = PlayerPose.AdvanceAction(0.125, 0.5, 0.15, frozen: true);
Check("diálogo congela o avanço das ações", pausedAction == (0.125, 0.25, 0.25));
var resumedAction = PlayerPose.AdvanceAction(pausedAction.Elapsed, 0.5, 0.15, frozen: false);
Check("retomar ação cruza a marca de impacto uma vez",
    Ultima.AnimationTimeline.Crossed(resumedAction.Previous, resumedAction.Progress, false, [0.5]).SequenceEqual(new[] { 0 }));

Console.WriteLine(failures == 0 ? "\nTODOS OS TESTES PASSARAM" : $"\n{failures} FALHA(S)");
return failures == 0 ? 0 : 1;

sealed class Scripted : IRandomSource
{
    private readonly Queue<int> _values;
    public Scripted(params int[] values) => _values = new Queue<int>(values);
    public int Die(int sides) => _values.Dequeue();
}
