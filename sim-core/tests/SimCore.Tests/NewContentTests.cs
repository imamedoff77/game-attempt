using SimCore;
using Xunit;

public class NewContentTests
{
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    private static World NewBase()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10, 0);
        w.PlaceBuilding(BuildingKind.PowerPlant, 20, 10, 0);
        w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
        return w;
    }

    private static void UpgradeToLevel2(World w, int team = 0)
    {
        w.GiveResources(team, 1000);
        w.Enqueue(new UpgradeBaseCommand(), team);
        Run(w, 41);
        Assert.Equal(2, w.Player(team).BaseLevel);
    }

    [Fact]
    public void Sniper_NeedsBaseLevel2()
    {
        var w = NewBase();
        Building barracks = w.PlaceBuilding(BuildingKind.Barracks, 30, 20, 0);
        w.GiveResources(0, 1000);
        Assert.Equal(QueueCheck.LevelTooLow, w.CheckQueue(barracks, UnitKind.Sniper));
        Assert.Equal(QueueCheck.Ok, w.CheckQueue(barracks, UnitKind.Rifleman));

        UpgradeToLevel2(w);
        Assert.Equal(QueueCheck.Ok, w.CheckQueue(barracks, UnitKind.Sniper));
    }

    [Fact]
    public void ArmoredCar_NeedsBaseLevel2()
    {
        var w = NewBase();
        Building factory = w.PlaceBuilding(BuildingKind.VehicleFactory, 30, 30, 0);
        w.GiveResources(0, 1000);
        Assert.Equal(QueueCheck.LevelTooLow, w.CheckQueue(factory, UnitKind.ArmoredCar));
        UpgradeToLevel2(w);
        Assert.Equal(QueueCheck.Ok, w.CheckQueue(factory, UnitKind.ArmoredCar));
    }

    [Fact]
    public void Tower_CannotBeBuilt_BeforeLevel2()
    {
        var w = NewBase();
        w.GiveResources(0, 1000);
        Assert.Equal(BuildCheck.LevelTooLow, w.CheckBuild(BuildingKind.Tower, 30, 30, 0));
        UpgradeToLevel2(w);
        Assert.Equal(BuildCheck.Ok, w.CheckBuild(BuildingKind.Tower, 30, 30, 0));
    }

    [Fact]
    public void Tower_ShootsEnemiesInRange_AndIgnoresFriends()
    {
        var w = NewBase();
        w.PlaceBuilding(BuildingKind.Tower, 30, 30, 0);
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(36, 31), 1); // a tank survives long enough to measure the damage
        Unit friend = w.SpawnUnit(UnitKind.Rifleman, P(24, 31), 0); // out of the enemy tank's sight

        Run(w, 3);

        Assert.True(enemy.Hp < enemy.MaxHp);
        Assert.Equal(friend.MaxHp, friend.Hp);
    }

    [Fact]
    public void Tower_IgnoresEnemiesOutOfRange()
    {
        var w = NewBase();
        w.PlaceBuilding(BuildingKind.Tower, 30, 30, 0);
        Unit far = w.SpawnUnit(UnitKind.Rifleman, P(45, 45), 1);
        Run(w, 3);
        Assert.Equal(far.MaxHp, far.Hp);
    }

    [Fact]
    public void Tower_UnderConstruction_DoesNotShoot()
    {
        var w = NewBase();
        w.GiveResources(0, 1000);
        UpgradeToLevel2(w);
        w.Enqueue(new BuildCommand(BuildingKind.Tower, 30, 30), 0);
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(35, 31), 1);
        Run(w, 5); // 25 s to build
        Assert.Equal(enemy.MaxHp, enemy.Hp);
    }

    [Fact]
    public void Tower_WithoutEnergy_DoesNotShoot()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10, 0);
        w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
        w.PlaceBuilding(BuildingKind.Barracks, 20, 10, 0); // takes 30 of the HQ's 20 energy
        w.PlaceBuilding(BuildingKind.Tower, 30, 30, 0);
        Assert.True(w.Player(0).EnergyShortage);
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(35, 31), 1);
        Run(w, 5);
        Assert.Equal(enemy.MaxHp, enemy.Hp);
    }

    [Fact]
    public void Tower_KillsAWeakUnit()
    {
        var w = NewBase();
        w.PlaceBuilding(BuildingKind.Tower, 30, 30, 0);
        w.SpawnUnit(UnitKind.Rifleman, P(36, 31), 1);
        Run(w, 10);
        Assert.Empty(w.Units);
    }

    [Fact]
    public void RepairStation_HealsFriendlyUnitsNearby_NotFarOnes()
    {
        var w = NewBase();
        w.PlaceBuilding(BuildingKind.RepairStation, 30, 30, 0);
        Unit near = w.SpawnUnit(UnitKind.Tank, P(33, 31), 0);
        Unit far = w.SpawnUnit(UnitKind.Tank, P(45, 45), 0);
        foreach (Unit t in new[] { near, far }) w.DamageUnit(t.Id, t.MaxHp - 100); // 100 left

        Run(w, 5);

        Assert.True(near.Hp >= 150);
        Assert.Equal(100, far.Hp);
    }

    [Fact]
    public void RepairStation_NeverHealsAboveMax()
    {
        var w = NewBase();
        w.PlaceBuilding(BuildingKind.RepairStation, 30, 30, 0);
        Unit u = w.SpawnUnit(UnitKind.Rifleman, P(33, 31), 0);
        w.DamageUnit(u.Id, 3);
        Run(w, 5);
        Assert.Equal(u.MaxHp, u.Hp);
    }

    [Fact]
    public void NewContent_IsDeterministic()
    {
        ulong Hash()
        {
            var w = NewBase();
            w.PlaceBuilding(BuildingKind.Tower, 30, 30, 0);
            w.PlaceBuilding(BuildingKind.RepairStation, 24, 24, 0);
            for (int i = 0; i < 6; i++) w.SpawnUnit(i % 2 == 0 ? UnitKind.Sniper : UnitKind.ArmoredCar, P(34 + i, 32), 1);
            Run(w, 20);
            return w.StateHash();
        }
        Assert.Equal(Hash(), Hash());
    }

    // ---- damage depends on what is hit (data: vsInfantryMilli / vsVehicleMilli / vsBuildingMilli) ----

    /// <summary>The health lost by the first hit a standing attacker lands on a target (a unit kind or a building).</summary>
    private static int FirstHit(UnitKind attacker, UnitKind? targetUnit)
    {
        var w = NewBase();
        Unit a = w.SpawnUnit(attacker, P(30, 30), 0);
        Unit t = null; Building b = null;
        if (targetUnit != null) t = w.SpawnUnit(targetUnit.Value, P(33, 30), 1);
        else b = w.PlaceBuilding(BuildingKind.PowerPlant, 34, 29, 1);
        for (int i = 0; i < 3 * World.TicksPerSecond; i++)
        {
            w.Step();
            if (t != null && t.Hp < t.MaxHp) return t.MaxHp - t.Hp;
            if (b != null && b.Hp < b.MaxHp) return b.MaxHp - b.Hp;
            if (t == null && b == null) break;
            if (a.AttackKind == AttackKind.None && b != null) w.Enqueue(new AttackCommand(new[] { a.Id }, AttackKind.Building, b.Id), 0);
        }
        return 0;
    }

    [Fact]
    public void Damage_DependsOnWhatIsHit()
    {
        Assert.Equal(6, FirstHit(UnitKind.Rifleman, UnitKind.Rifleman));
        Assert.Equal(6 * Defs.Combat(UnitKind.Rifleman).VsVehicleMilli / 1000, FirstHit(UnitKind.Rifleman, UnitKind.Tank));
        Assert.Equal(25 * Defs.Combat(UnitKind.RocketTrooper).VsVehicleMilli / 1000, FirstHit(UnitKind.RocketTrooper, UnitKind.Tank));
        Assert.Equal(25 * Defs.Combat(UnitKind.RocketTrooper).VsInfantryMilli / 1000, FirstHit(UnitKind.RocketTrooper, UnitKind.Rifleman));
        Assert.True(Defs.Combat(UnitKind.RocketTrooper).VsVehicleMilli > Defs.Combat(UnitKind.Rifleman).VsVehicleMilli);
    }

    [Fact]
    public void RiflesAreWeakAgainstBuildings_TanksAreStrong()
    {
        int rifle = FirstHit(UnitKind.Rifleman, null);
        int tank = FirstHit(UnitKind.Tank, null);
        Assert.Equal(System.Math.Max(1, 6 * Defs.Combat(UnitKind.Rifleman).VsBuildingMilli / 1000), rifle);
        Assert.Equal(50 * Defs.Combat(UnitKind.Tank).VsBuildingMilli / 1000, tank);
    }

    [Fact]
    public void DamageMultipliers_AreInTheDataHash_AndDefaultToFull()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gamedata.json"));
        string changed = json.Replace("\"vsVehicleMilli\": 2000", "\"vsVehicleMilli\": 2100");
        Assert.NotEqual(json, changed);
        Assert.NotEqual(GameData.Parse(json).Hash, GameData.Parse(changed).Hash);
        Assert.Equal(1000, Defs.Combat(UnitKind.Tank).VsInfantryMilli); // not listed for the tank: full damage
    }
}
