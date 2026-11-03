using SimCore;

namespace SimCore.Tests;

public class EconomyTests
{
    private static World NewWorld()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10);
        return w;
    }

    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    [Fact]
    public void Start_HasStartingResourcesAndBaseEnergy()
    {
        World w = NewWorld();
        Assert.Equal(Economy.StartResources, w.Resources);
        Assert.Equal(20, w.EnergyCapacity);
        Assert.Equal(0, w.EnergyUsed);
        Assert.Equal(20, w.IncomePerMinute);
    }

    [Fact]
    public void BaseIncome_Is20PerMinute()
    {
        World w = NewWorld();
        Run(w, 60);
        Assert.Equal(Economy.StartResources + 20, w.Resources);
        Run(w, 120);
        Assert.Equal(Economy.StartResources + 60, w.Resources);
    }

    [Fact]
    public void Build_CostsResources_AndPowerPlantAddsEnergy()
    {
        World w = NewWorld();
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 20, 10));
        w.Step();

        Assert.Equal(Economy.StartResources - 100, w.Resources);
        Assert.Equal(20 + 120, w.EnergyCapacity);
        Assert.Equal(2, w.Buildings.Count);
    }

    [Fact]
    public void PowerPlants_GiveLessEnergyEachTime()
    {
        World w = NewWorld();
        w.PlaceBuilding(BuildingKind.PowerPlant, 20, 10);
        w.PlaceBuilding(BuildingKind.PowerPlant, 24, 10);
        w.PlaceBuilding(BuildingKind.PowerPlant, 28, 10);
        Assert.Equal(20 + 120 + 80 + 60, w.EnergyCapacity);
    }

    [Fact]
    public void SupplyCenter_RaisesIncome_WhenPowered()
    {
        World w = NewWorld();
        w.PlaceBuilding(BuildingKind.PowerPlant, 20, 10);
        w.PlaceBuilding(BuildingKind.SupplyCenter, 24, 10);
        Assert.Equal(20 + 60, w.IncomePerMinute);
        Assert.Equal(20, w.EnergyUsed);
    }

    [Fact]
    public void EnergyShortage_StopsConsumers_ButNotBaseOrPowerPlants()
    {
        World w = NewWorld(); // capacity 20
        Building barracks = w.PlaceBuilding(BuildingKind.Barracks, 20, 10); // needs 30
        Building power = w.PlaceBuilding(BuildingKind.PowerPlant, 30, 10);  // +120 -> fine again
        Assert.True(barracks.Powered);

        // Without the power plant the barracks would not run.
        var w2 = NewWorld();
        Building b2 = w2.PlaceBuilding(BuildingKind.Barracks, 20, 10);
        Assert.True(w2.EnergyShortage);
        Assert.False(b2.Powered);
        Assert.Equal(20, w2.IncomePerMinute);

        Building s2 = w2.PlaceBuilding(BuildingKind.SupplyCenter, 30, 10);
        Assert.False(s2.Powered);
        Assert.Equal(20, w2.IncomePerMinute); // supply center gives nothing while unpowered
        Assert.True(power.Powered);
    }

    [Fact]
    public void Build_IsIgnored_WhenTooExpensive_OrBlocked()
    {
        World w = NewWorld();
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 20, 10));   // 100
        w.Enqueue(new BuildCommand(BuildingKind.VehicleFactory, 30, 10)); // 300
        w.Enqueue(new BuildCommand(BuildingKind.Barracks, 40, 10));       // 200 -> total 600 > 500, ignored
        w.Step();
        Assert.Equal(3, w.Buildings.Count);
        Assert.Equal(100, w.Resources);

        // On top of the base: blocked, and not charged.
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 11, 11));
        w.Step();
        Assert.Equal(3, w.Buildings.Count);
        Assert.Equal(100, w.Resources);
    }

    [Fact]
    public void CheckBuild_ReportsReason()
    {
        World w = NewWorld();
        Assert.Equal(BuildCheck.Ok, w.CheckBuild(BuildingKind.PowerPlant, 20, 10));
        Assert.Equal(BuildCheck.BadGround, w.CheckBuild(BuildingKind.PowerPlant, 11, 11));
        Assert.Equal(BuildCheck.NotAllowed, w.CheckBuild(BuildingKind.Hq, 30, 30));

        w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(21), Fix64.FromInt(11)));
        Assert.Equal(BuildCheck.UnitInWay, w.CheckBuild(BuildingKind.PowerPlant, 20, 10));
    }

    [Fact]
    public void Economy_IsDeterministic()
    {
        ulong Hash()
        {
            World w = NewWorld();
            w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 20, 10));
            w.Enqueue(new BuildCommand(BuildingKind.SupplyCenter, 25, 10));
            Run(w, 90);
            return w.StateHash();
        }
        Assert.Equal(Hash(), Hash());
    }

    [Fact]
    public void NewBuilding_ReroutesUnitsWalkingThroughIt()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(5), Fix64.FromInt(30)));
        w.Enqueue(new MoveCommand(new[] { u.Id }, new Vec2(Fix64.FromInt(40), Fix64.FromInt(30))));
        w.Step();
        w.Enqueue(new BuildCommand(BuildingKind.VehicleFactory, 20, 28)); // right on the straight line
        for (int i = 0; i < 40 * World.TicksPerSecond && u.Moving; i++)
        {
            w.Step();
            int bid = w.Map.BuildingAt(u.Pos.X.ToInt(), u.Pos.Y.ToInt());
            Assert.Equal(0, bid);
        }
        Assert.False(u.Moving);
        Assert.Equal(40, u.Pos.X.ToInt());
    }
}
