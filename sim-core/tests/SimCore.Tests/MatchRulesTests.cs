using SimCore;

namespace SimCore.Tests;

/// <summary>Faza 2.1: construction time, unit limit (pop) and base levels.</summary>
public class MatchRulesTests
{
    private const int A = 0, B = 1;

    private static World NewWorld()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10, A);
        w.PlaceBuilding(BuildingKind.PowerPlant, 20, 10, A);
        w.PlaceBuilding(BuildingKind.Barracks, 30, 20, A);
        w.PlaceBuilding(BuildingKind.VehicleFactory, 30, 30, A);
        w.PlaceBuilding(BuildingKind.Hq, 50, 50, B);
        return w;
    }

    private static void SetResources(World w, int team, int amount) => w.GiveResources(team, amount - w.Player(team).Resources);
    private static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
    private static Building Barracks(World w) => w.Buildings.First(b => b.Kind == BuildingKind.Barracks && b.Team == A);

    // ---------------------------------------------------------------- construction

    [Fact]
    public void ABuildingOrderedByAPlayer_IsBuiltOverTime_AndOnlyThenWorks()
    {
        var w = NewWorld();
        int capacity = w.Player(A).EnergyCapacity;
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 24, 10));
        w.Step();
        Building plant = w.Buildings.Last();
        Assert.True(plant.UnderConstruction);
        Assert.Equal(capacity, w.Player(A).EnergyCapacity);      // gives nothing yet

        int total = Defs.Building(BuildingKind.PowerPlant).ConstructionSeconds * World.TicksPerSecond;
        Run(w, total - 2);
        Assert.True(plant.UnderConstruction);
        Run(w, 2);
        Assert.False(plant.UnderConstruction);
        Assert.Equal(plant.MaxHp, plant.Hp);                      // health grew with the work
        Assert.True(w.Player(A).EnergyCapacity > capacity);       // and now it gives energy
    }

    [Fact]
    public void ConstructionHealth_GrowsSteadily_FromATenth()
    {
        var w = NewWorld();
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 24, 10));
        w.Step();
        Building plant = w.Buildings.Last();
        Assert.InRange(plant.Hp, plant.MaxHp / 10, plant.MaxHp / 5);
        int last = plant.Hp;
        for (int i = 0; i < 100; i++)
        {
            w.Step();
            Assert.True(plant.Hp >= last);
            last = plant.Hp;
        }
        Assert.True(plant.Hp > plant.MaxHp / 10 + 20);
    }

    [Fact]
    public void ABuildingUnderConstruction_UsesNoEnergy_AndCannotProduce()
    {
        var w = NewWorld();
        SetResources(w, A, 2000);
        w.Enqueue(new BuildCommand(BuildingKind.Barracks, 40, 20));
        w.Step();
        Building second = w.Buildings.Last();
        Assert.True(second.UnderConstruction);
        Assert.Equal(Defs.Building(BuildingKind.Barracks).EnergyUse + Defs.Building(BuildingKind.VehicleFactory).EnergyUse, w.Player(A).EnergyUsed); // the new barracks adds nothing yet
        Assert.Equal(QueueCheck.NotAllowed, w.CheckQueue(second, UnitKind.Rifleman));
    }

    [Fact]
    public void Cancelling_Construction_RefundsHalf_AndFreesTheGround()
    {
        var w = NewWorld();
        w.Enqueue(new BuildCommand(BuildingKind.SupplyCenter, 40, 40));
        w.Step();
        Building b = w.Buildings.Last();
        int afterPaying = w.Player(A).Resources;
        w.Enqueue(new CancelBuildCommand(b.Id));
        w.Step();
        Assert.Equal(afterPaying + Defs.Building(BuildingKind.SupplyCenter).Cost * Economy.CancelRefundPercent / 100, w.Player(A).Resources);
        Assert.Null(w.GetBuilding(b.Id));
        Assert.Equal(BuildCheck.Ok, w.CheckBuild(BuildingKind.SupplyCenter, 40, 40, A)); // the spot is free again
    }

    [Fact]
    public void ACompletedBuilding_OrAnothersBuilding_CannotBeCancelled()
    {
        var w = NewWorld();
        Building barracks = Barracks(w);
        int before = w.Player(A).Resources;
        w.Enqueue(new CancelBuildCommand(barracks.Id), A);
        w.Step();
        Assert.NotNull(w.GetBuilding(barracks.Id));
        Assert.Equal(before, w.Player(A).Resources);

        SetResources(w, B, 500);
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 40, 50), B);
        w.Step();
        Building theirs = w.Buildings.Last();
        w.Enqueue(new CancelBuildCommand(theirs.Id), A);   // A tries to cancel B's construction
        w.Step();
        Assert.NotNull(w.GetBuilding(theirs.Id));
    }

    // ---------------------------------------------------------------- unit limit

    [Fact]
    public void TheUnitLimit_StopsQueuing_AndReservesPlacesForQueuedUnits()
    {
        var w = NewWorld();
        SetResources(w, A, 100000);
        Building barracks = Barracks(w);
        Assert.Equal(20, w.Player(A).PopCap);

        // Rocket troopers take 2 places each: ten fill the base, but a queue holds only 5 and units come out one by one.
        for (int i = 0; i < World.MaxQueue; i++) w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.RocketTrooper));
        w.Step();
        Assert.Equal(10, w.Player(A).PopUsed); // queued units already count
        Assert.Equal(QueueCheck.QueueFull, w.CheckQueue(barracks, UnitKind.RocketTrooper));

        // Fill the rest with tanks (4 places): 10 left = two tanks.
        Building factory = w.Buildings.First(b => b.Kind == BuildingKind.VehicleFactory);
        w.Enqueue(new QueueUnitCommand(factory.Id, UnitKind.Tank));
        w.Enqueue(new QueueUnitCommand(factory.Id, UnitKind.Tank));
        w.Step();
        Assert.Equal(18, w.Player(A).PopUsed);
        Assert.Equal(QueueCheck.PopCapReached, w.CheckQueue(factory, UnitKind.Tank));   // 18 + 4 > 20
        w.Enqueue(new QueueUnitCommand(factory.Id, UnitKind.Tank));
        w.Step();
        Assert.Equal(2, factory.Queue.Count);                                              // the third was refused
    }

    [Fact]
    public void ARefusedQueueOrder_CostsNothing()
    {
        var w = NewWorld();
        SetResources(w, A, 100000);
        Building factory = w.Buildings.First(b => b.Kind == BuildingKind.VehicleFactory);
        for (int i = 0; i < 5; i++) w.Enqueue(new QueueUnitCommand(factory.Id, UnitKind.Tank)); // 5 x 4 = 20 places: exactly the limit
        w.Step();
        Assert.Equal(20, w.Player(A).PopUsed);
        int before = w.Player(A).Resources;
        w.Enqueue(new QueueUnitCommand(Barracks(w).Id, UnitKind.Rifleman));
        w.Step();
        Assert.InRange(w.Player(A).Resources, before, before + 1); // at most one tick of income, no 50 charged
        Assert.Empty(Barracks(w).Queue);
    }

    [Fact]
    public void CancellingAQueuedUnit_FreesItsPlaces()
    {
        var w = NewWorld();
        Building factory = w.Buildings.First(b => b.Kind == BuildingKind.VehicleFactory);
        SetResources(w, A, 5000);
        w.Enqueue(new QueueUnitCommand(factory.Id, UnitKind.Tank));
        w.Step();
        Assert.Equal(4, w.Player(A).PopUsed);
        w.Enqueue(new CancelQueuedCommand(factory.Id, 0));
        w.Step();
        Assert.Equal(0, w.Player(A).PopUsed);
    }

    [Fact]
    public void ADeadUnit_FreesItsPlaces()
    {
        var w = NewWorld();
        Unit u = w.SpawnUnit(UnitKind.Tank, new Vec2(Fix64.FromInt(20), Fix64.FromInt(40)), A);
        w.Step();
        Assert.Equal(4, w.Player(A).PopUsed);
        // An enemy tank right next to it finishes it off.
        w.SpawnUnit(UnitKind.Tank, new Vec2(Fix64.FromInt(21), Fix64.FromInt(40)), B);
        for (int i = 0; i < 20 * 60 && w.GetUnit(u.Id) != null; i++) w.Step();
        Assert.Null(w.GetUnit(u.Id));
        w.Step();
        Assert.Equal(0, w.Player(A).PopUsed);
    }

    // ---------------------------------------------------------------- base levels

    [Fact]
    public void UpgradingTheBase_CostsMoney_TakesTime_AndRaisesTheLimit()
    {
        var w = NewWorld();
        SetResources(w, A, 1000);
        int cost = Economy.BaseUpgradeCost(1);
        Assert.Equal(UpgradeCheck.Ok, w.CheckUpgradeBase(A));
        w.Enqueue(new UpgradeBaseCommand(), A);
        w.Step();
        Assert.Equal(1000 - cost, w.Player(A).Resources);
        Assert.True(w.Player(A).Upgrading);
        Assert.Equal(1, w.Player(A).BaseLevel);
        Assert.Equal(UpgradeCheck.AlreadyUpgrading, w.CheckUpgradeBase(A));

        Run(w, Economy.BaseUpgradeSeconds(1) * World.TicksPerSecond);
        Assert.Equal(2, w.Player(A).BaseLevel);
        Assert.False(w.Player(A).Upgrading);
        Assert.Equal(40, w.Player(A).PopCap);
        Assert.Equal(20, w.Player(B).PopCap); // the other team is untouched
    }

    [Fact]
    public void Upgrade_NeedsMoney_AnHq_AndStopsAtTheTopLevel()
    {
        var w = NewWorld();
        SetResources(w, A, 10);
        Assert.Equal(UpgradeCheck.NotEnoughResources, w.CheckUpgradeBase(A));
        w.Enqueue(new UpgradeBaseCommand(), A);
        w.Step();
        Assert.False(w.Player(A).Upgrading);

        SetResources(w, A, 100000);
        for (int level = 1; level < Economy.MaxBaseLevel; level++)
        {
            w.Enqueue(new UpgradeBaseCommand(), A);
            Run(w, Economy.BaseUpgradeSeconds(level) * World.TicksPerSecond + 2);
        }
        Assert.Equal(Economy.MaxBaseLevel, w.Player(A).BaseLevel);
        Assert.Equal(UpgradeCheck.MaxLevel, w.CheckUpgradeBase(A));
    }

    // ---------------------------------------------------------------- network format and determinism

    [Fact]
    public void NewCommands_SurviveTheByteFormat()
    {
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new CancelBuildCommand(77)), out Command c));
        Assert.Equal(77, ((CancelBuildCommand)c).BuildingId);
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new UpgradeBaseCommand()), out c));
        Assert.IsType<UpgradeBaseCommand>(c);
    }

    [Fact]
    public void TwoWorlds_GivenTheSameOrders_StayIdentical()
    {
        World Play()
        {
            var w = NewWorld();
            SetResources(w, A, 3000);
            w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 24, 10), A);
            w.Enqueue(new UpgradeBaseCommand(), A);
            Run(w, 100);
            w.Enqueue(new BuildCommand(BuildingKind.SupplyCenter, 40, 40), A);
            w.Enqueue(new QueueUnitCommand(Barracks(w).Id, UnitKind.Rifleman), A);
            Run(w, 20);
            w.Enqueue(new CancelBuildCommand(w.Buildings.Last().Id), A);
            Run(w, 1500);
            return w;
        }
        Assert.Equal(Play().StateHash(), Play().StateHash());
    }
}
