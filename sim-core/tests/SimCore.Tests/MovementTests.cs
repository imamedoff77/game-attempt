using System.Collections.Generic;
using System.Diagnostics;
using SimCore;
using Xunit;
using Xunit.Abstractions;

public class MovementTests
{
    private readonly ITestOutputHelper _output;
    public MovementTests(ITestOutputHelper output) { _output = output; }

    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
    private static Vec2 P(double x, double y) => new Vec2(Fix64.FromFraction((int)(x * 1000), 1000), Fix64.FromFraction((int)(y * 1000), 1000));
    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    /// <summary>The smallest "distance minus the two radii" over all pairs: negative means they overlap.</summary>
    private static double WorstOverlap(World w)
    {
        double worst = double.MaxValue;
        var units = new List<Unit>(w.Units);
        for (int i = 0; i < units.Count; i++)
        {
            for (int j = i + 1; j < units.Count; j++)
            {
                double dx = units[i].Pos.X.ToFloat() - units[j].Pos.X.ToFloat();
                double dy = units[i].Pos.Y.ToFloat() - units[j].Pos.Y.ToFloat();
                double gap = System.Math.Sqrt(dx * dx + dy * dy) - (units[i].Radius.ToFloat() + units[j].Radius.ToFloat());
                if (gap < worst) worst = gap;
            }
        }
        return worst;
    }

    [Fact]
    public void UnitsOrderedToTheSamePoint_EndUpSideBySide_NotInsideEachOther()
    {
        var w = new World(new MapGrid(40, 40));
        var ids = new List<int>();
        for (int i = 0; i < 12; i++) ids.Add(w.SpawnUnit(UnitKind.Rifleman, P(5 + i % 4, 5 + i / 4)).Id);
        w.Enqueue(new MoveCommand(ids.ToArray(), P(25, 25)));

        Run(w, 20);

        Assert.True(WorstOverlap(w) > -0.05, "units overlap: " + WorstOverlap(w));
        foreach (Unit u in w.Units) Assert.False(u.Moving);
    }

    [Fact]
    public void Tanks_KeepTheirDistance()
    {
        var w = new World(new MapGrid(40, 40));
        var ids = new List<int>();
        for (int i = 0; i < 6; i++) ids.Add(w.SpawnUnit(UnitKind.Tank, P(5 + i, 5)).Id);
        w.Enqueue(new MoveCommand(ids.ToArray(), P(25, 25)));

        Run(w, 2); // they start 1 apart, tanks are 1.2 wide: let them spread first
        double worst = double.MaxValue;
        for (int i = 0; i < 40 * World.TicksPerSecond; i++)
        {
            w.Step();
            worst = System.Math.Min(worst, WorstOverlap(w));
        }
        Assert.True(worst > -0.15, "worst overlap " + worst);
    }

    [Fact]
    public void FortyUnits_PassANarrowGap_AndAllArrive()
    {
        var map = new MapGrid(60, 30);
        for (int y = 0; y < 30; y++)
            if (y < 14 || y > 15) map.SetTerrain(30, y, TerrainType.Rock); // a wall with a two-cell gap
        var w = new World(map);
        var ids = new List<int>();
        for (int i = 0; i < 40; i++) ids.Add(w.SpawnUnit(UnitKind.Rifleman, P(5 + i % 8, 8 + i / 8 * 2)).Id);
        w.Enqueue(new MoveCommand(ids.ToArray(), P(50, 15)));

        Run(w, 60);

        int across = 0;
        foreach (Unit u in w.Units) if (u.Pos.X.ToInt() > 30) across++;
        Assert.Equal(40, across);
        foreach (Unit u in w.Units) Assert.False(u.Moving);
    }

    [Fact]
    public void FortyUnits_PassAOneCellCorridor_InSingleFile()
    {
        var map = new MapGrid(60, 20);
        for (int y = 0; y < 20; y++)
            if (y != 10) for (int x = 25; x <= 35; x++) map.SetTerrain(x, y, TerrainType.Rock);
        var w = new World(map);
        var ids = new List<int>();
        for (int i = 0; i < 40; i++) ids.Add(w.SpawnUnit(UnitKind.Rifleman, P(3 + i % 8, 5 + i / 8 * 2)).Id);
        w.Enqueue(new MoveCommand(ids.ToArray(), P(50, 10)));

        Run(w, 90);

        int across = 0;
        foreach (Unit u in w.Units) if (u.Pos.X.ToInt() > 35) across++;
        Assert.Equal(40, across);
    }

    [Fact]
    public void TwoGroupsMeetingHeadOn_DoNotWalkThroughEachOther()
    {
        var w = new World(new MapGrid(60, 30));
        var left = new List<int>();
        var right = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            left.Add(w.SpawnUnit(UnitKind.Rifleman, P(5, 10 + i), 0).Id);
            right.Add(w.SpawnUnit(UnitKind.Rifleman, P(50, 10 + i), 1).Id);
        }
        w.Enqueue(new MoveCommand(left.ToArray(), P(50, 15)), 0);
        w.Enqueue(new MoveCommand(right.ToArray(), P(5, 15)), 1);

        double worst = double.MaxValue;
        for (int i = 0; i < 40 * World.TicksPerSecond; i++)
        {
            w.Step();
            worst = System.Math.Min(worst, WorstOverlap(w));
        }
        Assert.True(worst > -0.15, "worst overlap " + worst);
    }

    [Fact]
    public void Units_NeverEndUpOnABlockedCell()
    {
        var map = new MapGrid(30, 30);
        for (int y = 10; y < 20; y++) map.SetTerrain(15, y, TerrainType.Rock);
        var w = new World(map);
        var ids = new List<int>();
        for (int i = 0; i < 30; i++) ids.Add(w.SpawnUnit(UnitKind.Rifleman, P(3 + i % 5, 12 + i / 5)).Id);
        w.Enqueue(new MoveCommand(ids.ToArray(), P(27, 15)));

        for (int i = 0; i < 30 * World.TicksPerSecond; i++)
        {
            w.Step();
            foreach (Unit u in w.Units)
                Assert.True(w.Map.IsWalkable(u.Pos.X.ToInt(), u.Pos.Y.ToInt()), "unit " + u.Id + " on a blocked cell at tick " + w.Tick);
        }
    }

    [Fact]
    public void TheFrontUnit_TakesTheFrontSlot_SoTheGroupDoesNotCrossItself()
    {
        var w = new World(new MapGrid(60, 40));
        Unit back = w.SpawnUnit(UnitKind.Rifleman, P(5, 20));
        Unit front = w.SpawnUnit(UnitKind.Rifleman, P(15, 20));
        w.Enqueue(new MoveCommand(new[] { back.Id, front.Id }, P(50, 20)));
        w.Step();

        // Same row: the unit already ahead goes to the slot nearer the destination.
        Assert.True(front.Target.X >= back.Target.X);
    }

    [Fact]
    public void ManyRandomOrders_GiveTheSameHashOnTwoWorlds()
    {
        World a = RandomMovementWorld(), b = RandomMovementWorld();
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    private static World RandomMovementWorld()
    {
        var map = new MapGrid(64, 64);
        for (int i = 10; i < 40; i++) map.SetTerrain(32, i, TerrainType.Rock);
        var w = new World(map);
        var rng = new DeterministicRandom(12345);
        var ids = new List<int>();
        UnitKind[] kinds = { UnitKind.Rifleman, UnitKind.RocketTrooper, UnitKind.Jeep, UnitKind.Tank };
        for (int i = 0; i < 60; i++)
            ids.Add(w.SpawnUnit(kinds[i % 4], P(4 + rng.Next(20), 4 + rng.Next(50)), i % 2).Id);

        for (int round = 0; round < 100; round++)
        {
            var group = new List<int>();
            int team = rng.Next(2);
            foreach (int id in ids) if (rng.Next(4) == 0 && w.GetUnit(id)?.Team == team) group.Add(id);
            if (group.Count > 0) w.Enqueue(new MoveCommand(group.ToArray(), P(2 + rng.Next(60), 2 + rng.Next(60))), team);
            for (int t = 0; t < 10; t++) w.Step();
        }
        return w;
    }

    [Fact]
    public void EightyMovingUnits_TickStaysCheap()
    {
        var w = new World(new MapGrid(100, 100));
        var rng = new DeterministicRandom(7);
        var ids = new List<int>();
        for (int i = 0; i < 80; i++) ids.Add(w.SpawnUnit(UnitKind.Rifleman, P(10 + rng.Next(30), 10 + rng.Next(30)), i % 2).Id);
        for (int i = 0; i < 80; i += 8)
            w.Enqueue(new MoveCommand(ids.GetRange(i, 8).ToArray(), P(20 + rng.Next(60), 20 + rng.Next(60))), ids[i] % 2);

        for (int i = 0; i < 40; i++) w.Step(); // warm up the JIT
        var sw = Stopwatch.StartNew();
        const int ticks = 400;
        for (int i = 0; i < ticks; i++)
        {
            if (i % 100 == 0)
                for (int g = 0; g < 80; g += 8)
                    w.Enqueue(new MoveCommand(ids.GetRange(g, 8).ToArray(), P(20 + rng.Next(60), 20 + rng.Next(60))), ids[g] % 2);
            w.Step();
        }
        double ms = sw.Elapsed.TotalMilliseconds / ticks;
        _output.WriteLine($"80 units: {ms:F3} ms per tick");
        Assert.True(ms < 4.0, $"{ms} ms per tick");
    }
}
