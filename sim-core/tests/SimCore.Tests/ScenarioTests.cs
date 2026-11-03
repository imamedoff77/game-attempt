using System.Collections.Generic;
using SimCore;

namespace SimCore.Tests;

public class ScenarioTests
{
    private static int[] PlayerUnitIds(World w)
    {
        var ids = new List<int>();
        foreach (Unit u in w.Units) if (u.Team == World.TeamPlayer) ids.Add(u.Id);
        return ids.ToArray();
    }

    private static Building EnemyHq(World w)
    {
        foreach (Building b in w.Buildings) if (b.Team == World.TeamEnemy && b.Kind == BuildingKind.Hq) return b;
        return null;
    }

    [Fact]
    public void Scenario_HasEverythingOnWalkableGround_AndEnemyBaseIsReachable()
    {
        World w = DemoScenario.Create();
        Assert.Equal(14, w.CountAlive(World.TeamEnemy));
        foreach (Unit u in w.Units)
            Assert.True(w.Map.IsWalkable(u.Pos.X.ToInt(), u.Pos.Y.ToInt()), $"unit {u.Id} {u.Kind} at {u.Pos.X.ToInt()},{u.Pos.Y.ToInt()}");

        Unit squad = w.Units[0];
        var path = Pathfinder.FindPath(w.Map, squad.Pos.X.ToInt(), squad.Pos.Y.ToInt(), 16, 47);
        Assert.NotNull(path);
    }

    [Fact]
    public void StartingSquad_AttackingTheBaseTogether_BeatsTheGuards()
    {
        World w = DemoScenario.Create();
        w.Enqueue(new AttackCommand(PlayerUnitIds(w), AttackKind.Building, EnemyHq(w).Id));
        for (int i = 0; i < 6 * 60 * World.TicksPerSecond && EnemyHq(w) != null; i++) w.Step();

        int mine = 0;
        foreach (Unit u in w.Units) if (u.Team == World.TeamPlayer) mine++;
        System.Console.WriteLine($"After the fight: enemy HQ alive={EnemyHq(w) != null}, player units left={mine}, enemy left={w.CountAlive(World.TeamEnemy)}, tick={w.Tick}");
        Assert.Null(EnemyHq(w));
        Assert.True(mine > 0);
    }

    [Fact]
    public void Scenario_IsDeterministic()
    {
        ulong Run()
        {
            World w = DemoScenario.Create();
            w.Enqueue(new AttackCommand(PlayerUnitIds(w), AttackKind.Building, EnemyHq(w).Id));
            for (int i = 0; i < 60 * World.TicksPerSecond; i++) w.Step();
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
