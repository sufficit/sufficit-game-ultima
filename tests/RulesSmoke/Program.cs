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

WorldSky.Sample Sky(int month, int day, int hour, int minute = 0)
    => WorldSky.At(new DateTimeOffset(2026, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());

var summerSky = Sky(6, 21, 12);
var winterSky = Sky(12, 21, 12);
var midnightSky = Sky(12, 21, 0);
Check("solstícios mudam a altura do Sol em Greenwich",
    summerSky.Sun.Up is > .87 and < .90 && winterSky.Sun.Up is > .24 and < .28);
Check("o Sol nasce a leste, põe-se a oeste e fica abaixo do horizonte à meia-noite",
    Sky(6, 21, 5).Sun.East > .6 && Sky(6, 21, 19).Sun.East < -.6
    && midnightSky.Sun.Up < 0 && midnightSky.DirectSun == 0 && midnightSky.Daylight == 0);
Check("o Sol permanece acima do horizonte por mais tempo no verão",
    Sky(6, 21, 19).Sun.Up > 0 && Sky(12, 21, 17).Sun.Up < 0);
// Instantes publicados pelo US Naval Observatory para as fases principais de 2026.
Check("fases principais da Lua conferem com o almanaque",
    Sky(1, 18, 19, 52) is { Phase: 0, Illumination: < .02 }
    && Sky(1, 26, 4, 47) is { Phase: 2, Illumination: > .47 and < .53 }
    && Sky(1, 3, 10, 3) is { Phase: 4, Illumination: > .98 }
    && Sky(1, 10, 15, 48) is { Phase: 6, Illumination: > .47 and < .53 });
Check("perfis de clima separam chuva de neblina e mantêm compatibilidade da preferência",
    WorldWeather.Find("fog") is { Rain: 0, Fog: > 2 }
    && WorldWeather.Find("rain").Rain == 1
    && WorldWeather.Find("heavy-rain").Rain > 1
    && WorldWeather.Find("inválido").Id == "clear");

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

// IA de NPCs: decisões sobre snapshot com budget por tick (docs/activities/202611220040-ia-npcs-decisoes.md)
WorldSnapshot Snap(bool near = true, bool open = false, bool ai = false, bool nearWanderer = false) => new(0, -12, near, nearWanderer, open, ai, 1000);
DialogueTopicsSmoke.Run(Check);
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
Check("conteúdo remoto preserva a conversa que está aberta", npc.Tick(Snap(open: true)).Count == 0);
npc.Submit(new DialogClosedByPlayer(1901));
npc.Tick(Snap(open: true));
npc.Submit(new PlayerInteracted(1902));
var intro = npc.Tick(Snap(open: false)).Single();
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
var cameraSamples = volumes.TraceCamera(10, -2, 0, 1, 0, 0, 10.13, 0.28);
var expectedCameraSamples = new byte[cameraSamples.Length];
for (var i = 0; i < expectedCameraSamples.Length; i++)
{
    var x = 10 + Math.Min((i + 1) * 0.25, 10.13);
    if (!volumes.Near(x, 0)) continue;
    expectedCameraSamples[i] = (byte)(volumes.Blocks(x, -2, 0, 0.28) ? 2 : 1);
    if (expectedCameraSamples[i] == 2) break;
}
Check("câmera preserva a ordem das amostras de túnel e superfície",
    cameraSamples.SequenceEqual(expectedCameraSamples) && cameraSamples.Contains((byte)2));
Check("raio longe de túneis dispensa amostras volumétricas",
    volumes.TraceCamera(10, 0, 40, 1, 0, 0, 10, 0.28).Length == 0);
Check("teto só é criado quando todo o perímetro tem cobertura",
    heightfield.CanCoverCarve(0, 0, 2, -0.5) && !heightfield.CanCoverCarve(0, 0, 2, 0));

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
Check("translação é integral quando rumo e corpo estão alinhados", PlayerMovement.TurnTravelScale(0) == 1);
Check("mudança de rumo modera avanço lateral e inversão",
    Math.Abs(PlayerMovement.TurnTravelScale(Math.PI / 4) - 0.25) < 1e-12
    && PlayerMovement.TurnTravelScale(Math.PI / 2) < 1e-12
    && PlayerMovement.TurnTravelScale(Math.PI) == 0);
var pivotSurface = new TerrainSurface();
var pivotVolumes = new TerrainVolumes(pivotSurface);
var pivotMovement = new PlayerMovement(1.75, .41, .03);
double PivotBeforeAlignment(bool obstructed)
{
    pivotMovement.Stop();
    pivotMovement.SetColliders(obstructed ? [0, -.6, .28, 0, 5, 0] : []);
    var z = 0d;
    var yaw = 0d;
    for (var step = 0; step < 60; step++)
    {
        var pose = pivotMovement.Advance(0, .03, z, yaw, 0, 1, 0, -1, 0, 0, false, .01,
            pivotSurface, pivotVolumes, false);
        if (Math.Abs(pose.Yaw) >= Math.PI * .96) break;
        (z, yaw) = (pose.Z, pose.Yaw);
    }
    return -z;
}
var pivotAdvance = PivotBeforeAlignment(false);
var pivotObstructed = PivotBeforeAlignment(true);
Check("a primeira passada começa durante o giro e respeita o colisor",
    pivotAdvance > .10 && pivotObstructed is > .05 and < .075);

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
var playerInput = new PlayerInput();
Span<double> inputFrame = stackalloc double[7];
playerInput.SetKey("KeyW", true);
playerInput.SetKey("ArrowUp", true);
playerInput.SetKey("ShiftRight", true);
playerInput.Read(inputFrame);
Check("WASD movement combines with arrow-key look and sprint",
    inputFrame[1] == 1 && inputFrame[5] == -1 && inputFrame[6] == 1);
playerInput.SetKey("KeyW", false);
playerInput.SetKey("ArrowRight", true);
playerInput.Read(inputFrame);
Check("Arrow keys turn and tilt the camera without moving",
    inputFrame[0] == 0 && inputFrame[1] == 0 && inputFrame[4] == 1 && inputFrame[5] == -1);
playerInput.SetKey("ArrowUp", false);
playerInput.SetKey("ArrowRight", false);
playerInput.SetKey("KeyW", true);
playerInput.SetKey("KeyS", true);
playerInput.SetTouch(false, 0.1, 0.1);
playerInput.SetGamepad(0.1, -0.1, 0.1, -0.1, 0, 0);
playerInput.Read(inputFrame);
Check("teclas opostas e ruído dos analógicos não deslocam o jogador",
    inputFrame[..6].SequenceEqual(new double[6]));
playerInput.SetTouch(true, 1, 0);
playerInput.SetGamepad(0, -1, 0, 0.5, 1, 0);
playerInput.Read(inputFrame);
Check("controle e toque combinam movimento e órbita",
    inputFrame[2] == 1 && inputFrame[3] == 1 && inputFrame[4] == 1 && inputFrame[5] > 0);
playerInput.Clear(keepPad: true);
playerInput.Read(inputFrame);
Check("pausa limpa as teclas e o toque sem descartar a leitura do controle",
    inputFrame[2] == 1 && inputFrame[4] == 0 && inputFrame[6] == 0);
playerInput.Clear();
playerInput.Read(inputFrame);
Check("perda de foco limpa todos os dispositivos", inputFrame.SequenceEqual(new double[7]));
Check("alcance de coleta considera os três eixos", PlayerPose.CanCollect(0, 2.1, 2.8)
    && !PlayerPose.CanCollect(0, 2.2, 2.8));

Span<double> legs = stackalloc double[7];
PlayerPose.SolveLegs(legs, 0, 0.03, 0.03, 0.03, 0.77, 0.364, 0.051,
    0.1, 0, 0.22, true, -0.1, 0, 0.04, true);
Check("desnível eleva quadril com limite e mantém as solas niveladas", legs[0] > 0.03 && legs[0] <= 0.12
    && Math.Abs(legs[1] + legs[2] + legs[3]) < 1e-12 && Math.Abs(legs[4] + legs[5] + legs[6]) < 1e-12);

Span<double> gait = stackalloc double[12];
Span<double> feet = stackalloc double[10];
var walked = 0.0;
var phase = 0.0;
var maxContactDrift = 0.0;
foreach (var step in new[] { 0.01, 0.02, 0.035, 0.045, 0.055, 0.06 })
{
    walked += step;
    PlayerPose.Animate(gait, phase, 1, 0, step, 0.01, 1, false, false, 0, 0, 0);
    phase = gait[0];
    PlayerPose.FootTargets(feet, 0, phase, gait[1], step / 0.01, 0.077, 0.06);
    PlayerPose.SolveLegs(legs, 0, 0.03, 0.01, 0.03, 0.77, 0.364, 0.051,
        feet[0], feet[1], 0, false, feet[4], feet[5], 0, false, feet[8], feet[9]);
    var ankleForward = -0.364 * (Math.Sin(legs[1]) + Math.Sin(legs[1] + legs[2]));
    maxContactDrift = Math.Max(maxContactDrift, Math.Abs(walked + ankleForward));
}
Check("sola de apoio resiste à aceleração e à troca de velocidade em linha reta", maxContactDrift < 1e-9);
PlayerPose.FootTargets(feet, 0, Math.PI * 0.75, 1, 3.5, 0.077, 0.06);
var toePitch = feet[8];
PlayerPose.FootTargets(feet, 0, Math.PI * 1.25, 1, 3.5, 0.077, 0.06);
Check("tornozelo alterna inclinação na saída e na preparação do contato", toePitch > 0.1 && feet[8] < -0.1);

foreach (var kind in new[] { "humano", "canino", "equino", "ave" })
{
    var structure = PlayerAnatomy.Create(kind, 1.75);
    var parents = new HashSet<string> { "root" };
    Check("estrutura ordenada de " + kind, structure.All(joint => parents.Contains(joint.Parent) && parents.Add(joint.Name)));
}
var human = PlayerAnatomy.Create("humano", 1.75).ToDictionary(joint => joint.Name);
Check("humano liga tornozelo ao joelho e falanges aos punhos",
    human["ankle-left"].Parent == "knee-left" && human["finger-5-right"].Parent == "wrist-right"
    && human["toe-4-right-tip"].Parent == "toe-4-right");

var contacts = new PlayerFootContacts();
Span<double> alignment = stackalloc double[4];
contacts.Begin(0, 0, 0, 0);
PlayerPose.FootTargets(feet, 0, 0, 1, 3.5, 0.091, 0.06);
contacts.Apply(feet, alignment, 0, 0.091, 0.06);
var plantedX = feet[2];
var plantedZ = feet[3];
const double cornerYaw = 0.65;
contacts.Begin(0.13, 0.025, cornerYaw, 0.14);
PlayerPose.FootTargets(feet, cornerYaw, 0.6, 1, 3.5, 0.091, 0.06);
contacts.Apply(feet, alignment, 0.6, 0.091, 0.06);
Check("sola guarda posição e rumo mundiais na curva",
    Math.Abs(0.13 + feet[2] - plantedX) < 1e-12 && Math.Abs(0.025 + feet[3] - plantedZ) < 1e-12
    && Math.Abs(alignment[2] + cornerYaw) < 1e-12 && Math.Abs(alignment[0]) > 0.01);
Check("o plano do joelho acompanha a sola sem inverter na fronteira angular",
    PlayerPose.KneeSwivel(0) == 0 && PlayerPose.KneeSwivel(alignment[2]) < -0.3
    && PlayerPose.KneeSwivel(-alignment[2]) > 0.3
    && Math.Abs(PlayerPose.KneeSwivel(Math.PI - 0.001) - PlayerPose.KneeSwivel(-Math.PI + 0.001)) < 0.002);
Span<double> legs3D = stackalloc double[9];
PlayerPose.SolveLegs(legs3D, 0, .03, .02, .03, .77, .364, .051,
    feet[0], feet[1], 0, false, feet[4], feet[5], 0, false, feet[8], feet[9], alignment[0], alignment[1]);
var lateralReach = .364 * (Math.Cos(legs3D[1]) + Math.Cos(legs3D[1] + legs3D[2])) * Math.Sin(legs3D[7]);
Check("inclinação do quadril alcança o alvo lateral sem mover o contato", Math.Abs(lateralReach - alignment[0]) < 1e-9);
contacts.Begin(10, 20, 0, 0);
PlayerPose.FootTargets(feet, 0, 0, 1, 3.5, 0.091, 0.06);
contacts.Apply(feet, alignment, 0, 0.091, 0.06);
Check("teleporte solta os apoios anteriores", Math.Abs(feet[2] - plantedX) < 1e-12 && Math.Abs(feet[3] - plantedZ) < 1e-12);

const double swingStart = Math.PI / 2;
var playerStride = PlayerFootContacts.StrideLength;
contacts.Reset();
contacts.Begin(0, 0, 0, 0);
PlayerPose.FootTargets(feet, 0, swingStart, 1, 3.5, .091, .06, playerStride);
contacts.Apply(feet, alignment, swingStart, .091, .06);
var toeOff = feet[3];
var advance = playerStride * .075;
contacts.Begin(0, 0, 0, .001);
PlayerPose.FootTargets(feet, 0, swingStart + .001, 1, 3.5, .091, .06, playerStride);
contacts.Apply(feet, alignment, swingStart + .001, .091, .06, .016);
contacts.Begin(0, advance, 0, advance);
var earlySwing = swingStart + Math.PI * .15;
PlayerPose.FootTargets(feet, 0, earlySwing, 1, 3.5, .091, .06, playerStride);
contacts.Apply(feet, alignment, earlySwing, .091, .06);
Check("pé começa a avançar em relação ao corpo após a saída do apoio", feet[3] > toeOff);
PlayerPose.Animate(gait, Math.PI / 2, 1, 0, 0, 0, 0, false, false, 0, 0, 0, playerStride, playerGait: true);
Check("braço esquerdo avança quando a perna direita aterrissa", gait[10] < -.3 && gait[11] > .3);

contacts.Reset();
var midSwing = Math.PI * .75;
contacts.Begin(0, 0, 0, .02);
PlayerPose.FootTargets(feet, 0, midSwing, 1, 3.5, .091, .06, playerStride);
contacts.Apply(feet, alignment, midSwing, .091, .06, .02);
var swingHeight = feet[1];
var previousFeet = feet[..8].ToArray();
var stepKeepsSupport = true;
for (var i = 0; i < 25; i++)
{
    contacts.Begin(0, 0, 0, 0);
    PlayerPose.FootTargets(feet, 0, midSwing, 1, 0, .091, .06, playerStride);
    stepKeepsSupport &= !contacts.Apply(feet, alignment, midSwing, .091, .06, .02)
        && (feet[1] == 0 || feet[5] == 0);
    for (var foot = 0; foot <= 4; foot += 4)
    {
        if (feet[foot + 1] != 0 || previousFeet[foot + 1] != 0) continue;
        stepKeepsSupport &= double.Hypot(feet[foot + 2] - previousFeet[foot + 2],
            feet[foot + 3] - previousFeet[foot + 3]) < 1e-12;
    }
    feet[..8].CopyTo(previousFeet);
}
Check("parar no meio do balanço reequilibra o quadril com um pé plantado de cada vez",
    swingHeight > .05 && stepKeepsSupport && Math.Abs(feet[0]) < 1e-12 && Math.Abs(feet[4]) < 1e-12
    && feet[1] == 0 && feet[5] == 0 && feet[8] == 0 && feet[9] == 0);
contacts.Begin(0, .02, 0, .02);
PlayerPose.FootTargets(feet, 0, midSwing, 1, 1, .091, .06, playerStride);
contacts.Apply(feet, alignment, midSwing, .091, .06, .02);
Check("voltar a andar eleva gradualmente a sola", feet[1] > 0 && feet[1] < swingHeight / 2);
PlayerPose.Animate(gait, midSwing, 0, .5, 0, .02, 1, false, false, 0, 0, 0, playerStride, playerGait: true);
Check("tronco em repouso mantém pose neutra", gait[2] == 0 && gait[3] == 0);

var turnKeepsSupport = true;
var maxPivotWidth = 0d;
foreach (var turn in new[] { Math.PI, -Math.PI })
{
    contacts.Reset();
    contacts.Begin(0, 0, 0, 0);
    contacts.Apply(feet, alignment, 0, .091, .06, .01);
    for (var step = 1; step <= 84; step++)
    {
        var progress = step / 84d;
        var yaw = PlayerMovement.PivotYaw(0, turn, progress);
        contacts.Begin(0, 0, yaw, 0);
        contacts.ApplyPivot(feet, alignment, .091, .06, 0, turn, progress);
        turnKeepsSupport &= feet[1] == 0 || feet[5] == 0;
        maxPivotWidth = Math.Max(maxPivotWidth, double.Hypot(feet[6] - feet[2], feet[7] - feet[3]));
    }
    turnKeepsSupport &= feet[1] == 0 && feet[5] == 0 && Math.Abs(feet[0]) < .04 && Math.Abs(feet[4]) < .04;
}
Check("inversão em ambos os sentidos alterna apoio com base compacta",
    turnKeepsSupport && maxPivotWidth < .28);

const double minimumGap = PlayerCharacterSpace.TorsoDepth * 2;
var occupied = new PlayerCharacterSpace();
occupied.Set(PlayerCharacterSpace.Guardian, 0, 0, 2);
var blockedByActor = occupied.Move(PlayerCharacterSpace.Player, 0, 0, 0, 0, 6);
Check("varredura impede atravessar personagem em alta velocidade",
    blockedByActor.Blocked && Math.Abs(blockedByActor.Z - (2 - minimumGap - 0.002)) < 1e-5);
Check("atores acima do alcance vertical podem se cruzar no plano",
    !occupied.Move(PlayerCharacterSpace.Player, 0, 3, 0, 0, 6).Blocked);
var actorSpawn = occupied.Place(3, 0, 0, 2);
Check("pose de rede sobreposta é separada sem impor o raio antigo",
    double.Hypot(actorSpawn.X, actorSpawn.Z - 2) >= minimumGap && double.Hypot(actorSpawn.X, actorSpawn.Z - 2) < 0.55);
var tangent = occupied.Move(PlayerCharacterSpace.Player, 0, 0, 1.65, 1, 2.15);
Check("contato deixa avançar pela tangente", tangent.Blocked && tangent.X > 0.8);
occupied.Set(PlayerCharacterSpace.Player, 0, 0, 0);
occupied.SetHitboxes([
    PlayerCharacterSpace.Guardian, 0.25, 0.40, 0, 0.07, 0, 0, 0, 0.35, 0, 0, 0, 0.07,
    PlayerCharacterSpace.Player, 0.25, 0.40, 0, 0.07, 0, 0, 0, 0.35, 0, 0, 0, 0.07
]);
var legsOnly = occupied.Move(PlayerCharacterSpace.Player, 0, 0, 0, 0, 3);
Check("duas malhas finas aproximam até o contato real de suas caixas",
    legsOnly.Blocked && Math.Abs(legsOnly.Z - 1.858) < 1e-5);
Check("partes desencontradas deixam passagem",
    !occupied.Move(PlayerCharacterSpace.Player, 0.2, 0, 0, 0.2, 3).Blocked);
occupied.SetHitboxes([
    PlayerCharacterSpace.Guardian, 0, .90, 0, 0, 0, -.23, 0, .79, 0, .17, 0, 0,
    PlayerCharacterSpace.Player, 0, .90, 0, .23, 0, 0, 0, .79, 0, 0, 0, .17
]);
var sideBody = occupied.Move(PlayerCharacterSpace.Player, 0, 0, 0, 0, 3);
Check("aproximação lateral usa o semieixo girado junto com a malha",
    sideBody.Blocked && Math.Abs(sideBody.Z - 1.598) < 1e-5);
var animatedBodies = new PlayerCharacterSpace();
animatedBodies.Set(PlayerCharacterSpace.Player, 0, 0, 0);
animatedBodies.Set(0, 0.62, 0, 0);
animatedBodies.SetHitboxes([
    PlayerCharacterSpace.Player, .31, 1.03, 0, .06, 0, 0, 0, .21, 0, 0, 0, .05,
    0, -.31, 1.03, 0, .06, 0, 0, 0, .21, 0, 0, 0, .05
]);
var poseCorrection = animatedBodies.ResolveAnimatedContacts();
Check("braços que entram em contato na pose são corrigidos sem deslocar o jogador",
    poseCorrection.Length == 3 && poseCorrection[0] == 0 && double.Hypot(poseCorrection[1] - .62, poseCorrection[2]) >= .099);
Check("a resolução da pose estabiliza quando o contato some", animatedBodies.ResolveAnimatedContacts().Length == 0);
var articulated = new PlayerCharacterSpace();
articulated.Set(PlayerCharacterSpace.Player, 0, 0, 0);
articulated.Set(0, 0.62, 0, 0);
double[] occupiedArm = [0, -.31, 1.03, 0, .06, 0, 0, 0, .21, 0, 0, 0, .05, 1];
articulated.SetHitboxes([
    PlayerCharacterSpace.Player, .31, 1.03, 0, .06, 0, 0, 0, .21, 0, 0, 0, .05, 2,
    .. occupiedArm
], tagged: true);
Check("detecção de pose identifica os membros em contato", articulated.CollectLimbContacts().SequenceEqual([-1, 2, 0, 1]));
Check("validação rejeita uma articulação ainda sobreposta", !articulated.TryLimbPose(0, 1, occupiedArm));
var retractedArm = occupiedArm.ToArray();
retractedArm[1] = -.16;
Check("braço retraído resolve contato sem separar raízes",
    articulated.TryLimbPose(0, 1, retractedArm) && articulated.ResolveAnimatedContacts().Length == 0);
Span<double> shortenedLeg = stackalloc double[4];
PlayerPose.SolveSingleLeg(shortenedLeg, .53, .12, .04, .36, .10);
var solvedForward = -.36 * (Math.Sin(shortenedLeg[0]) + Math.Sin(shortenedLeg[0] + shortenedLeg[1]));
Check("passo encurtado alcança o alvo com o quadril fixo",
    Math.Abs(solvedForward - .12) < 1e-9 && shortenedLeg[1] > 0
    && Math.Abs(shortenedLeg[0] + shortenedLeg[1] + shortenedLeg[2] - .10) < 1e-9);

var recovering = new PlayerMovement(1.75, .41, .03);
recovering.Trip(1);
Check("tropeço tem reação e recuperação finita", recovering.Recover(.16) > 0.8 && recovering.Recover(.48) == 0);

contacts.Reset();
contacts.Begin(0, 0, 0, 0);
PlayerPose.FootTargets(feet, 0, 0, 1, 0, .091, .06, playerStride);
contacts.Apply(feet, alignment, 0, .091, .06);
contacts.Begin(0, 0, Math.PI, 0);
PlayerPose.FootTargets(feet, Math.PI, 0, 1, 0, .091, .06, playerStride);
Check("cruzamento de pernas levanta o pé e separa o apoio",
    contacts.Apply(feet, alignment, 0, .091, .06, .016) && feet[5] > 0.05 && alignment[1] - alignment[0] + 0.182 >= 0.17);

var cameraObstacles = new PlayerCameraObstacles();
cameraObstacles.Set([
    1, -1, 2, 1, 5, -1, 6, 1, -3, -1, -2, 1, 1, 4, 2, 5, 1, -5, 2, -4
], 0.28);
Check("índice da câmera preserva a folga nas bordas da região",
    cameraObstacles.Query(0, 1.2, 3, 1.5).SequenceEqual(new[] { 0 }));
Check("índice da câmera descarta as malhas das regiões vizinhas",
    cameraObstacles.Query(-4, -0.5, 0, 0.5).SequenceEqual(new[] { 2 }));
cameraObstacles.Set([], 0.28);
Check("remoção de malhas limpa o índice da câmera", cameraObstacles.Query(-4, -4, 4, 4).Length == 0);

var camera = new PlayerCamera();
Span<double> orbit = stackalloc double[9];
camera.Advance(orbit, -Math.PI / 2, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0, 0);
Check("órbita conserva o raio e o sentido inicial",
    Math.Abs(double.Hypot(double.Hypot(orbit[3], orbit[4]), orbit[5]) - 7.5) < 1e-12 && orbit[5] < 0 && orbit[7] == 1);
camera.Advance(orbit, 0, 1.12, 1.25, 40, 0.04, true, 1, 1, 20, -200, -12000, 0);
Check("câmera congelada mantém arrasto, limita zoom e amortece retorno",
    orbit[0] == -0.20 && orbit[1] == 0.18 && orbit[2] == 2.4 && orbit[6] > 1.25 && orbit[6] < 7.5 && orbit[7] == 0);
camera.Reset();
camera.Advance(orbit, 0, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0, 0);
Check("reiniciar restaura o zoom da câmera", orbit[2] == 7.5 && orbit[6] == 7.5);

camera.Advance(orbit, 0, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0, 0.1);
Check("a câmera amortece um degrau sem deslocar a órbita", orbit[8] is > 0 and < 0.1 && orbit[6] == 7.5);
for (var i = 0; i < 40; i++)
    camera.Advance(orbit, 0, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0, 0.1);
Check("a câmera alcança a nova altura e volta a repousar", orbit[8] == 0.1 && orbit[7] == 1);
camera.Advance(orbit, 0, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0, -2);
Check("a câmera limita a distância vertical em uma descida", Math.Abs(orbit[8] + 1.76) < 1e-12);
camera.ResetFollow();
camera.Advance(orbit, 0, 1.12, 7.5, 40, 0.04, false, 0, 0, 0, 0, 0, 5);
Check("teletransporte reposiciona a câmera sem arrasto vertical", orbit[8] == 5);

var closeCamera = new PlayerCamera();
Span<double> closeOrbit = stackalloc double[12];
Check("Camera cycle enters first person, then close third person",
    closeCamera.Cycle(0) == PlayerCameraView.FirstPerson &&
    closeCamera.Cycle(Math.PI / 2) == PlayerCameraView.Close);
closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 0, 0);
Check("Close camera starts behind the avatar at its own distance",
    closeOrbit[3] < -2 && Math.Abs(closeOrbit[5]) < 1e-9 && closeOrbit[6] == 7.5);
closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 100, 25, 0, 0);
Check("Close mouse look changes aim without altering elevated orbit angles or zoom",
    Math.Abs(closeOrbit[10] - (Math.PI / 2 + .4)) < 1e-9 &&
    closeOrbit[4] > .6 && closeOrbit[0] == 0 && closeOrbit[1] == 1.12 && closeOrbit[2] == 7.5);
closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, -1200, 0);
Check("Close zoom eases toward the nearer limit and keeps rendering until settled",
    CameraDistance(closeOrbit) is > 1.85 and < 2.15 && closeOrbit[7] == 0 && closeOrbit[2] == 7.5);
for (var i = 0; i < 40; i++)
    closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 0, 0);
Check("Close camera settles at the nearer limit",
    Math.Abs(CameraDistance(closeOrbit) - 1.85) < 1e-9 && closeOrbit[7] == 1);
Check("Camera cycle restores the elevated orbit",
    closeCamera.Cycle(0) == PlayerCameraView.Rear && double.IsNaN(closeCamera.AimYaw));

closeCamera.Cycle(0);
closeCamera.Cycle(0);
closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 0, 0);
Check("Close zoom persists when switching views", Math.Abs(CameraDistance(closeOrbit) - 1.85) < 1e-9);

closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 120, 0);
for (var i = 0; i < 40; i++)
    closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 0, 0);
Check("A standard wheel step changes close distance by 0.08 m",
    Math.Abs(CameraDistance(closeOrbit) - 1.93) < 1e-9);
closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 4800, 0);
for (var i = 0; i < 40; i++)
    closeCamera.Advance(closeOrbit, 0, 1.12, 7.5, 40, .04, false, 0, 0, 0, 0, 0, 0);
Check("Close zoom settles at the expanded far limit while preserving elevated zoom",
    Math.Abs(CameraDistance(closeOrbit) - 3.80) < 1e-9 && closeOrbit[2] == 7.5 && closeOrbit[7] == 1);

static double CameraDistance(ReadOnlySpan<double> pose) =>
    double.Hypot(double.Hypot(pose[3], pose[4]), pose[5]);

var aimMovement = new PlayerMovement(1.75, .41, .03);
var backpedal = aimMovement.Advance(0, .03, 0, 0, 0, 1, 0, -1, 0, 0, false, .04,
    pivotSurface, pivotVolumes, false, (x, z, dx, dz) => (x + dx, z + dz), aimYaw: 0);
Check("Close aim can backpedal without pivoting the character",
    backpedal.Z < -.01 && backpedal.Yaw == 0 && aimMovement.PivotProgress < 0);

var pausedAction = PlayerPose.AdvanceAction(0.125, 0.5, 0.15, frozen: true);
Check("diálogo congela o avanço das ações", pausedAction == (0.125, 0.25, 0.25));
var resumedAction = PlayerPose.AdvanceAction(pausedAction.Elapsed, 0.5, 0.15, frozen: false);
Check("retomar ação cruza a marca de impacto uma vez",
    Ultima.AnimationTimeline.Crossed(resumedAction.Previous, resumedAction.Progress, false, [0.5]).SequenceEqual(new[] { 0 }));

var paletteSurface = new TerrainSurface();
var palette = new TerrainPalette(paletteSurface);
var raster = palette.Rasterize(0, 0, 128, 4, detailed: false);
uint hash = 2166136261;
foreach (var value in raster) hash = unchecked((hash ^ value) * 16777619);
Check("paleta procedural coincide com os pixels do build JavaScript", hash == 1797590622);

var leftPalette = palette.Rasterize(0, 0, 128, 4, detailed: true);
var rightPalette = palette.Rasterize(1, 0, 128, 4, detailed: true);
const int paletteSide = 136;
const int row = paletteSide * 4;
var edgeMatches = true;
for (var plane = 0; plane < 3; plane++)
for (var j = 0; j < paletteSide; j++)
{
    var offset = (plane * paletteSide + j) * row;
    edgeMatches &= leftPalette.AsSpan(offset + 128 * 4, 8 * 4).SequenceEqual(rightPalette.AsSpan(offset, 8 * 4));
}
Check("os três planos compartilham os texels da divisa", edgeMatches);

palette.SetPaints([75, 75, 5, 3]);
Check("pintura distante não altera a paleta do chunk", palette.Rasterize(0, 0, 128, 4, false).SequenceEqual(raster));
palette.SetPaints([5, 0, 4, 3, 5, 0, 4, 5]);
Check("a última pintura decide o material da coleta", palette.MaterialLabel(5, 0, 0) == "Água");
palette.SetPaints([5, 0, 4, 3]);
Check("ao retirar água, a coleta usa a pintura anterior", palette.MaterialLabel(5, 0, 0) == "Rocha");
palette.SetPaints([]);
paletteSurface.AddDig(5, 0);
Check("a escavação expõe terra sob a grama", palette.MaterialLabel(5, paletteSurface.Height(5, 0), 0) == "Terra");

static uint Fingerprint(ReadOnlySpan<byte> bytes)
{
    uint value = 2166136261;
    foreach (var b in bytes) value = unchecked((value ^ b) * 16777619);
    return value;
}

const int textureSize = 128;
const int colorBytes = textureSize * textureSize * 4;
var bark = TerrainSceneryTextures.Bark(textureSize);
Check("casca procedural preserva pixels do JavaScript", Fingerprint(bark.AsSpan(0, colorBytes)) == 2978609026);
Check("folhagem procedural preserva pixels do JavaScript",
    Fingerprint(TerrainSceneryTextures.Foliage(textureSize)) == 1901876341);
Check("normais de água preservam pixels do JavaScript",
    Fingerprint(TerrainSceneryTextures.Water(textureSize)) == 881805511);
Check("relevo de casca preserva pixels do JavaScript",
    Fingerprint(TerrainSceneryTextures.Bump(MemoryMarshal.Cast<byte, float>(bark.AsSpan(colorBytes)),
        textureSize, 3.2)) == 2991320888);

var vertices = new float[] { 1, -1, 0, 1, 2, 0 };
var prepared = MemoryMarshal.Cast<byte, float>(TerrainMeshEffects.Prepare(
    MemoryMarshal.AsBytes(vertices.AsSpan()), [1, 0.08, 123, 2, 0.38, 456]));
Check("efeitos de malha preservam altura na estria e empacotam RGBA",
    prepared.Length == 14 && prepared[1] == -1 && prepared[4] == 2 &&
    prepared[9] == 1 && prepared[13] == 1 && prepared[6] < prepared[10]);

var scatter = new double[TerrainScatter.Header + TerrainScatter.Batch * TerrainScatter.Channels];
TerrainScatter.Fill(scatter, new TerrainSurface(), 123, 30, 1200, false, false, 0, -14, 5, 14, 0.55, 0.7, 1.5, 0);
var firstScatter = (double[])scatter.Clone();
TerrainScatter.Fill(scatter, new TerrainSurface(), 123, 30, 1200, false, false, 0, -14, 5, 14, 0.55, 0.7, 1.5, 0);
Check("espalhamento em lote é determinístico", scatter.SequenceEqual(firstScatter) && scatter[2] == TerrainScatter.Batch);
TerrainScatter.Fill(scatter, new TerrainSurface(), (uint)firstScatter[0], 6, (int)firstScatter[1],
    false, false, 0, -14, 5, 14, 0.55, 0.7, 1.5, 0);
Check("espalhamento retoma o estado e fecha a quantidade final", scatter[2] == 6 && scatter[1] < firstScatter[1]);

var crowd = new TerrainCrowd(new TerrainSurface(), 4, [1.5, 1.8]);
var inView = crowd.Cull(0, 0, 0, new double[24], 720, 0.85, 60);
Check("multidão próxima preenche o LOD detalhado", inView[0] == 4 && (inView[4] & 1) == 0);
var behindPlane = crowd.Cull(0, 0, 0, [0, 0, 0, -2], 720, 0.85, 60);
Check("multidão fora do frustum sai do lote", behindPlane[0] == 0 && behindPlane[2] == 4);
var far = crowd.Cull(100, 0, 0, new double[24], 720, 0.85, 60);
Check("multidão distante preserva o contador de descarte", far[0] == 0 && far[1] == 4);

var patrol = PlayerCompanionMotion.Patrol(5, 1.4, Math.PI / 2, 5, 0, 0, 0.05);
Check("Andarilho conserva raio e velocidade angular da patrulha",
    Math.Abs(double.Hypot(patrol.X - 5, patrol.Z) - 1.4) < 1e-12 && patrol.Phase == 0.0325 && patrol.Distance > 0);
var turnAcrossPi = PlayerCompanionMotion.Follow(1, 2, 3, 2, 4, -3, 0.05);
Check("remoto interpola deslocamento e atravessa pi pelo arco curto",
    turnAcrossPi.X > 1 && turnAcrossPi.X < 2 && turnAcrossPi.Yaw > 3
    && Math.Abs(turnAcrossPi.Distance - double.Hypot(turnAcrossPi.X - 1, turnAcrossPi.Z - 2)) < 1e-12);
Check("remoto no destino mantém a pose e percorre zero",
    PlayerCompanionMotion.Follow(1, 2, 3, 1, 2, 3, 0.05) == new PlayerCompanionMotion.Frame(1, 2, 3, 0, 0));

var remoteSurface = new TerrainSurface();
var sample = new PlayerRemoteAvatars.GroundSample();
var baseHeight = sample.Sample(remoteSurface, 0, 0);
Check("amostra de remoto distingue coordenadas exatas",
    sample.Sample(remoteSurface, 0.25, 0.5) == remoteSurface.Height(0.25, 0.5));
sample.Sample(remoteSurface, 0, 0);
remoteSurface.AddDig(0, 0);
Check("amostra de remoto invalida após escavação",
    sample.Sample(remoteSurface, 0, 0) < baseHeight && sample.Sample(remoteSurface, 0, 0) == remoteSurface.Height(0, 0));

var avatars = new PlayerRemoteAvatars(remoteSurface, 0.03, 0.82, 0.39, 0.06, 0.16, 0.05);
avatars.Add(7, 8, 0, baseHeight, 0, 0);
Check("primeira pose remota ajusta posição imediatamente", avatars.Target(7, 0, 0, 0));
var initialRemoteY = avatars.Tick(0.02, 0, 0)[2];
var nearRemoteFlags = PlayerRemoteAvatars.PoseReady | PlayerRemoteAvatars.NearPlayer;
Check("LOD remoto mantém a pose perto e dentro da faixa de histerese",
    avatars.Tick(0.02, 29, 0)[5] == nearRemoteFlags);
Check("LOD remoto longe conserva a pose entre atualizações",
    avatars.Tick(0.02, 33, 0)[5] == 0 && avatars.Tick(0.02, 29, 0)[5] == 0);
Check("novo destino próximo interpola sem teleporte", !avatars.Target(7, 2, 0, 1));
var remoteMoving = avatars.Tick(0.02, 29, 0);
Check("remoto distante interpola raiz sem recalcular a pose",
    remoteMoving[1] > 0 && remoteMoving[1] < 2 && remoteMoving[4] > 0 && remoteMoving[5] == 0);
Check("LOD remoto atualiza pose depois de acumular 100 ms",
    avatars.Tick(0.04, 31, 0)[5] == PlayerRemoteAvatars.PoseReady);
remoteSurface.SetEdits([0, 0, 0, 4, 1.5]);
var remoteAfterEdit = avatars.Tick(0.02, 35, 0);
Check("edição força o apoio remoto longe antes do próximo intervalo",
    remoteAfterEdit[5] == PlayerRemoteAvatars.PoseReady && remoteAfterEdit[2] > initialRemoteY);
Check("remoto voltando ao raio de entrada retoma a pose por quadro",
    avatars.Tick(0.02, 25, 0)[5] == nearRemoteFlags && avatars.Tick(0.02, 26, 0)[5] == nearRemoteFlags);
Check("teleporte distante força uma pose nova",
    avatars.Target(7, 50, 0, 2) && avatars.Tick(0.02, 0, 0)[1] == 50 && avatars.Tick(0.02, 0, 0)[5] == 0);
avatars.Add(20, 6, 2, baseHeight, 3, 0);
var remoteBatch = avatars.Tick(0.02, 0, 0);
Check("lote tem uma linha por avatar e mantém o mesmo buffer entre quadros",
    remoteBatch.Length == 2 * PlayerRemoteAvatars.Stride && remoteBatch[0] == 7
    && remoteBatch[PlayerRemoteAvatars.Stride] == 20 && ReferenceEquals(remoteBatch, avatars.Tick(0.02, 0, 0)));
avatars.Remove(7);
var remainingRemote = avatars.Tick(0.02, 0, 0);
Check("desconexão remove somente a linha do avatar",
    remainingRemote.Length == PlayerRemoteAvatars.Stride && remainingRemote[0] == 20);

Console.WriteLine(failures == 0 ? "\nTODOS OS TESTES PASSARAM" : $"\n{failures} FALHA(S)");
return failures == 0 ? 0 : 1;

sealed class Scripted : IRandomSource
{
    private readonly Queue<int> _values;
    public Scripted(params int[] values) => _values = new Queue<int>(values);
    public int Die(int sides) => _values.Dequeue();
}
