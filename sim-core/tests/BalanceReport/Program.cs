using System.Diagnostics;
using SimCore;
using SimCore.Tests;

// Balance report: plays computer players against each other (no graphics) and prints winners, match length and what got built.
//   dotnet run --project sim-core/tests/BalanceReport -c Release -- [gamedata.json] [games=8] [maxMinutes=60] [pairing ...]
// A pairing is e.g. Hard-Medium (team 0 level - team 1 level); with none given, all nine are played.
string dataPath = args.Length > 0 && args[0].EndsWith(".json") ? args[0] : Path.Combine(AppContext.BaseDirectory, "gamedata.json");
string[] rest = args.Where(a => !a.EndsWith(".json")).ToArray();
int games = rest.Length > 0 && int.TryParse(rest[0], out int g) ? g : 8;
int maxMinutes = rest.Length > 1 && int.TryParse(rest[1], out int m) ? m : 60;
var pairings = new List<(AiDifficulty, AiDifficulty)>();
foreach (string a in rest.Skip(2))
{
    string[] parts = a.Split('-');
    pairings.Add((Enum.Parse<AiDifficulty>(parts[0]), Enum.Parse<AiDifficulty>(parts[1])));
}
if (pairings.Count == 0)
    foreach (AiDifficulty a in Enum.GetValues<AiDifficulty>())
        foreach (AiDifficulty b in Enum.GetValues<AiDifficulty>())
            if (a <= b) pairings.Add((a, b));

Defs.Load(File.ReadAllText(dataPath));
if (Environment.GetEnvironmentVariable("TEAMBIAS") != null)
{
    // TEAMBIAS=1: two equal computer players, no side swapping: how often does team 0 win?
    foreach (AiDifficulty level in Enum.GetValues<AiDifficulty>())
    {
        var res = new AiMatch.Result[32];
        Parallel.For(0, res.Length, i => res[i] = AiMatch.Run(level, level, (ulong)(500 + i), maxMinutes));
        Console.WriteLine($"{level}: team 0 wins {res.Count(r => r.Winner == 0)}, team 1 wins {res.Count(r => r.Winner == 1)}");
    }
    return;
}
if (Environment.GetEnvironmentVariable("SYMMETRY") != null)
{
    // SYMMETRY=1: mirrored armies with identical orders, no computer player: does the simulation itself favour a team?
    int[] wins = new int[3];
    for (int i = 0; i < 16; i++)
    {
        World w = SkirmishScenario.Create((ulong)(900 + i));
        string variant = Environment.GetEnvironmentVariable("SYMMETRY");
        bool team1First = variant.Contains('a'), enqueue1First = variant.Contains('b'), noJitter = variant.Contains('c');
        if (noJitter) w = SkirmishScenario.Create(0);
        for (int tt = 0; tt < 2; tt++)
            for (int k = 0; k < 20; k++)
            {
                int t = team1First ? 1 - tt : tt;
                Vec2 p0 = new Vec2(Fix64.FromFraction(2 * (14 + k % 5) + 1, 2), Fix64.FromFraction(2 * (26 + k / 5) + 1, 2));
                Vec2 pos = t == 0 ? p0 : new Vec2(Fix64.FromInt(SkirmishScenario.Size) - p0.X, Fix64.FromInt(SkirmishScenario.Size) - p0.Y);
                w.SpawnUnit(UnitKind.Rifleman, pos, t);
            }
        for (int tt = 0; tt < 2; tt++)
        {
            int t = enqueue1First ? 1 - tt : tt;
            Building enemyHq = w.Buildings.First(b => b.Team == 1 - t && b.Kind == BuildingKind.Hq);
            w.Enqueue(new AttackCommand(w.Units.Where(u => u.Team == t).Select(u => u.Id).ToArray(), AttackKind.Building, enemyHq.Id), t);
        }
        for (int tick = 0; tick < 120 * World.TicksPerSecond; tick++) w.Step();
        int hp0 = w.Units.Where(u => u.Team == 0).Sum(u => u.Hp), hp1 = w.Units.Where(u => u.Team == 1).Sum(u => u.Hp);
        wins[hp0 > hp1 ? 0 : hp1 > hp0 ? 1 : 2]++;
    }
    Console.WriteLine($"identical armies: team 0 ahead {wins[0]}, team 1 ahead {wins[1]}, equal {wins[2]}");
    return;
}
if (Environment.GetEnvironmentVariable("MIRROR") is string mn)
{
    // MIRROR=n: n riflemen per side, mirrored; reports the first tick at which team 1 is no longer the exact mirror image of team 0.
    int n = int.Parse(mn);
    World w = SkirmishScenario.Create(0);
    foreach (Unit u in w.Units.ToList()) w.DamageUnit(u.Id, 10000);
    w.Step();
    for (int k = 0; k < n; k++)
        for (int t = 0; t < 2; t++)
        {
            Vec2 p0 = new Vec2(Fix64.FromFraction(2 * (14 + k % 5) + 1, 2), Fix64.FromFraction(2 * (26 + k / 5) + 1, 2));
            w.SpawnUnit(UnitKind.Rifleman, t == 0 ? p0 : new Vec2(Fix64.FromInt(64) - p0.X, Fix64.FromInt(64) - p0.Y), t);
        }
    for (int t = 0; t < 2; t++)
    {
        Building hq = w.Buildings.First(b => b.Team == 1 - t && b.Kind == BuildingKind.Hq);
        w.Enqueue(new AttackCommand(w.Units.Where(u => u.Team == t).Select(u => u.Id).ToArray(), AttackKind.Building, hq.Id), t);
    }
    for (int tick = 0; tick < 60 * World.TicksPerSecond; tick++)
    {
        w.Step();
        var a = w.Units.Where(u => u.Team == 0).ToList();
        var b = w.Units.Where(u => u.Team == 1).ToList();
        bool same = a.Count == b.Count;
        for (int i = 0; same && i < a.Count; i++)
            same = a[i].Hp == b[i].Hp && (Fix64.FromInt(64) - a[i].Pos.X).Raw == b[i].Pos.X.Raw && (Fix64.FromInt(64) - a[i].Pos.Y).Raw == b[i].Pos.Y.Raw;
        if (!same)
        {
            Console.WriteLine($"first difference at tick {w.Tick}");
            for (int i = 0; i < System.Math.Min(a.Count, b.Count); i++)
                Console.WriteLine($"  unit {i}: team0 ({a[i].Pos.X.ToFloat():F4},{a[i].Pos.Y.ToFloat():F4}) hp {a[i].Hp}   team1 mirrored ({64 - b[i].Pos.X.ToFloat():F4},{64 - b[i].Pos.Y.ToFloat():F4}) hp {b[i].Hp}");
            return;
        }
    }
    Console.WriteLine("mirror holds for 60 s");
    return;
}
if (Environment.GetEnvironmentVariable("CAMPAIGN") != null)
{
    // CAMPAIGN=1: every mission played by a computer player of each skill, 4 seeds each: wins, stars, minutes.
    foreach (Mission mission in Campaign.Missions)
    {
        foreach (AiDifficulty level in Enum.GetValues<AiDifficulty>())
        {
            var runs = new MissionRun[4];
            Parallel.For(0, runs.Length, i => runs[i] = MissionBot.Play(mission.Number, level, (ulong)(i + 1), maxMinutes));
            int won = runs.Count(r => r.Status == MissionStatus.Won), lost = runs.Count(r => r.Status == MissionStatus.Lost);
            Console.WriteLine($"mission {mission.Number,2} player {level,-6} won {won}/4 lost {lost} unfinished {4 - won - lost}  stars {string.Join("", runs.Select(r => r.Stars))}  minutes {string.Join(" ", runs.Select(r => (r.Seconds / 60.0).ToString("F1")))}  losses {string.Join(" ", runs.Select(r => r.UnitsLost))}");
        }
    }
    return;
}
if (Environment.GetEnvironmentVariable("SOAK") != null)
{
    // SOAK=1: a long match (maxMinutes, use data with a huge headquarters so nobody wins) played twice; prints cost per 10 minutes, memory and whether both runs end with the same hash.
    var hashes = new ulong[2];
    for (int run = 0; run < 2; run++)
    {
        var sw = Stopwatch.StartNew();
        long last = 0;
        var r = AiMatch.Run(AiDifficulty.Hard, AiDifficulty.Medium, 77, maxMinutes, null, w =>
        {
            if (w.Tick % (10 * 60 * World.TicksPerSecond) != 0) return;
            long now = sw.ElapsedMilliseconds;
            Console.WriteLine($"run {run} minute {w.Tick / World.TicksPerSecond / 60,3}: {(now - last) / (10.0 * 60 * World.TicksPerSecond) :F3} ms/tick in the last 10 min, units {w.Units.Count}, buildings {w.Buildings.Count}, heap {GC.GetTotalMemory(false) / 1048576} MB");
            last = now;
        });
        hashes[run] = r.Hash;
        Console.WriteLine($"run {run} ended at tick {r.Ticks} winner {r.Winner} hash {r.Hash:X} in {sw.Elapsed.TotalSeconds:F0} s");
    }
    Console.WriteLine(hashes[0] == hashes[1] ? "hashes equal" : "HASHES DIFFER");
    return;
}
if (Environment.GetEnvironmentVariable("TRACE") is string tr)
{
    // TRACE=Hard-Easy: one match, a line every 30 seconds per team.
    string[] tp = tr.Split('-');
    var kinds = Enum.GetValues<UnitKind>();
    AiMatch.Run(Enum.Parse<AiDifficulty>(tp[0]), Enum.Parse<AiDifficulty>(tp[1]), 1000, maxMinutes, null, w =>
    {
        if (w.Tick % (30 * World.TicksPerSecond) != 0) return;
        for (int t = 0; t < 2; t++)
        {
            var p = w.Player(t);
            string army = string.Join(" ", kinds.Select(k => (k, n: w.Units.Count(u => u.Team == t && u.Kind == k))).Where(x => x.n > 0).Select(x => $"{x.k}{x.n}"));
            var hq = w.Buildings.FirstOrDefault(b => b.Team == t && b.Kind == BuildingKind.Hq);
            string blds = string.Join(" ", w.Buildings.Where(b => b.Team == t && b.Kind != BuildingKind.Hq).GroupBy(b => b.Kind).Select(g => $"{g.Key}{g.Count()}"));
            Console.WriteLine($"{w.Tick / World.TicksPerSecond / 60.0,5:F1}m T{t} $%d inc{p.IncomePerMinute} pop{p.PopUsed}/{p.PopCap} L{p.BaseLevel} hq{hq?.Hp} | {army} | {blds}".Replace("%d", p.Resources.ToString()));
        }
    });
    return;
}
Console.WriteLine($"data {dataPath}  hash {Defs.DataHash:X}  games/pairing {games}  limit {maxMinutes} min");

var total = new long[Enum.GetValues<UnitKind>().Length];
var clock = Stopwatch.StartNew();
foreach (var (a, b) in pairings)
{
    var results = new AiMatch.Result[games];
    var built = new long[total.Length];
    Parallel.For(0, games, i =>
    {
        // A pair of games shares one seed with the sides swapped, so a map advantage cannot hide behind the difficulty.
        bool swap = i % 2 == 1;
        var seen = new HashSet<int>();
        var mine = new long[total.Length];
        var r = AiMatch.Run(swap ? b : a, swap ? a : b, (ulong)(1000 + i / 2), maxMinutes, null, w =>
        {
            foreach (Unit u in w.Units)
                if (seen.Add(u.Id)) mine[(int)u.Kind]++;
        });
        results[i] = swap && r.Winner >= 0 ? r with { Winner = 1 - r.Winner } : r;
        lock (built) for (int k = 0; k < built.Length; k++) built[k] += mine[k];
    });

    int winsA = results.Count(r => r.Winner == 0), winsB = results.Count(r => r.Winner == 1), draws = results.Count(r => r.Winner < 0);
    double[] minutes = results.Select(r => r.Ticks / (double)World.TicksPerSecond / 60).OrderBy(x => x).ToArray();
    Console.WriteLine($"{a,-6} vs {b,-6}  {winsA}:{winsB}  unfinished {draws}  minutes avg {minutes.Average():F1} median {minutes[minutes.Length / 2]:F1} min {minutes[0]:F1} max {minutes[^1]:F1}");
    for (int k = 0; k < built.Length; k++) total[k] += built[k];
}

long sum = Math.Max(1, total.Sum());
Console.WriteLine("units built (all games): " + string.Join("  ", Enum.GetValues<UnitKind>().Select(k => $"{k} {total[(int)k]} ({100 * total[(int)k] / sum}%)")));
Console.WriteLine($"took {clock.Elapsed.TotalSeconds:F0} s");
