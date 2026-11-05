using SimCore;

namespace SimCore.Tests;

public class CombatTests
{
    private static World NewWorld() => new World(Fix64.FromInt(64), Fix64.FromInt(64));
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    [Fact]
    public void IdleUnits_ShootEnemiesInSight_AndKillThem()
    {
        World w = NewWorld();
        Unit tank = w.SpawnUnit(UnitKind.Tank, P(10, 10));
        Unit enemy = w.SpawnUnit(UnitKind.Rifleman, P(16, 10), World.TeamEnemy);
        Run(w, 10);
        Assert.Null(w.GetUnit(enemy.Id));
        Assert.True(tank.Hp > 0);
    }

    [Fact]
    public void MarchingUnit_ShootsEnemyInsideItsArc_AndKeepsWalking()
    {
        World w = NewWorld();
        Unit rifle = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(12, 14), World.TeamEnemy); // ahead and to the right, in range
        w.Enqueue(new MoveCommand(new[] { rifle.Id }, P(10, 40)));
        Run(w, 3);
        Assert.True(enemy.Hp < enemy.MaxHp, "the rifleman should have hit the tank while walking");
        Assert.True(rifle.Pos.Y.ToInt() > 12, "and it keeps walking instead of stopping to fight");
    }

    [Fact]
    public void MarchingUnit_DoesNotShootOutsideItsArc()
    {
        World w = NewWorld();
        Unit tank = w.SpawnUnit(UnitKind.Tank, P(10, 10));      // 90 degree arc, walking north
        Unit enemy = w.SpawnUnit(UnitKind.Rifleman, P(13, 10), World.TeamEnemy); // straight to its side
        w.Enqueue(new MoveCommand(new[] { tank.Id }, P(10, 40)));
        Run(w, 4);
        Assert.Equal(enemy.MaxHp, enemy.Hp);
    }

    [Fact]
    public void StandingUnit_TurnsToItsTarget_ThenShoots()
    {
        World w = NewWorld();
        Unit tank = w.SpawnUnit(UnitKind.Tank, P(10, 10));                       // faces +y at the start
        Unit enemy = w.SpawnUnit(UnitKind.Rifleman, P(13, 10), World.TeamEnemy); // to its side
        Run(w, 3);
        Assert.True(enemy.Hp < enemy.MaxHp || w.GetUnit(enemy.Id) == null);
        Assert.True(tank.Facing.X.Raw > 0, "it turned east");
    }

    [Fact]
    public void AttackOrder_ChasesIntoRange_AndDestroysBuilding_FreeingTheCells()
    {
        World w = NewWorld();
        Building hq = w.PlaceBuilding(BuildingKind.Hq, 40, 40, World.TeamEnemy);
        Unit tank = w.SpawnUnit(UnitKind.Tank, P(10, 10));
        w.Enqueue(new AttackCommand(new[] { tank.Id }, AttackKind.Building, hq.Id));
        Run(w, 400); // a headquarters is tough
        Assert.Null(w.GetBuilding(hq.Id));
        Assert.Equal(0, w.Map.BuildingAt(41, 41));
        Assert.True(w.Map.IsWalkable(41, 41));
        Assert.Equal(0, w.CountAlive(World.TeamEnemy));
    }

    [Fact]
    public void EnemyBuildings_DoNotChangeThePlayersEconomy()
    {
        World w = NewWorld();
        w.PlaceBuilding(BuildingKind.Hq, 10, 10);
        int cap = w.EnergyCapacity, used = w.EnergyUsed, income = w.IncomePerMinute;
        w.PlaceBuilding(BuildingKind.PowerPlant, 40, 40, World.TeamEnemy);
        w.PlaceBuilding(BuildingKind.Barracks, 45, 40, World.TeamEnemy);
        Assert.Equal(cap, w.EnergyCapacity);
        Assert.Equal(used, w.EnergyUsed);
        Assert.Equal(income, w.IncomePerMinute);
    }

    [Fact]
    public void CannotOrderAttackOnOwnUnit()
    {
        World w = NewWorld();
        Unit a = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        Unit b = w.SpawnUnit(UnitKind.Rifleman, P(11, 10));
        w.Enqueue(new AttackCommand(new[] { a.Id }, AttackKind.Unit, b.Id));
        w.Step();
        Assert.Equal(AttackKind.None, a.AttackKind);
    }

    [Fact]
    public void Guards_ChaseThenReturnHome()
    {
        World w = NewWorld();
        Unit guard = w.SpawnUnit(UnitKind.Rifleman, P(30, 30), World.TeamEnemy);
        w.SetGuard(guard);
        Unit intruder = w.SpawnUnit(UnitKind.Jeep, P(38, 30)); // inside sight (9), outside range (5)
        Run(w, 3);
        Assert.NotEqual(AttackKind.None, guard.AttackKind);

        // Intruder leaves: the guard gives up and walks home.
        w.Enqueue(new MoveCommand(new[] { intruder.Id }, P(60, 60)));
        Run(w, 40);
        Assert.Equal(AttackKind.None, guard.AttackKind);
        Assert.True((guard.Pos - guard.Home).SqrLength <= Fix64.FromInt(2));
    }

    [Fact]
    public void PatrollingGuard_WalksBetweenPoints()
    {
        World w = NewWorld();
        Unit jeep = w.SpawnUnit(UnitKind.Jeep, P(20, 20), World.TeamEnemy);
        w.SetGuard(jeep, new[] { P(20, 20), P(40, 20) });
        bool reachedFar = false;
        for (int i = 0; i < 20 * World.TicksPerSecond; i++)
        {
            w.Step();
            if (jeep.Pos.X.ToInt() >= 39) reachedFar = true;
        }
        Assert.True(reachedFar);
    }

    [Fact]
    public void DestroyedPlayerHq_IsDetected()
    {
        World w = NewWorld();
        Building hq = w.PlaceBuilding(BuildingKind.Hq, 10, 10);
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(20, 12), World.TeamEnemy);
        w.SetGuard(enemy);
        Run(w, 400);
        Assert.False(w.HasBuilding(BuildingKind.Hq, World.TeamPlayer));
    }

    [Fact]
    public void Combat_IsDeterministic()
    {
        ulong Hash()
        {
            World w = NewWorld();
            w.PlaceBuilding(BuildingKind.Barracks, 40, 40, World.TeamEnemy);
            for (int i = 0; i < 4; i++)
            {
                w.SpawnUnit(UnitKind.Rifleman, P(10 + i, 10));
                Unit g = w.SpawnUnit(UnitKind.Rifleman, P(36 + i, 38), World.TeamEnemy);
                w.SetGuard(g);
            }
            var ids = new System.Collections.Generic.List<int>();
            foreach (Unit u in w.Units) if (u.Team == 0) ids.Add(u.Id);
            w.Enqueue(new MoveCommand(ids.ToArray(), P(30, 35)));
            Run(w, 60);
            return w.StateHash();
        }
        Assert.Equal(Hash(), Hash());
    }
}
