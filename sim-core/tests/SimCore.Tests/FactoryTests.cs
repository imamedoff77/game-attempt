using SimCore;

namespace SimCore.Tests;

/// <summary>Building levels, door sides, the base's gathering point, "queue anywhere" load balancing and the base's growing income.</summary>
public class FactoryTests
{
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    private static World NewBase()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10, 0);
        w.PlaceBuilding(BuildingKind.PowerPlant, 20, 10, 0);
        w.PlaceBuilding(BuildingKind.PowerPlant, 22, 10, 0);
        w.PlaceBuilding(BuildingKind.PowerPlant, 24, 10, 0);
        w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
        w.GiveResources(0, 5000);
        return w;
    }

    [Fact]
    public void Factory_StartsAtLevel1_AndCannotBeRaisedBeforeTheBase()
    {
        var w = NewBase();
        Building b = w.PlaceBuilding(BuildingKind.Barracks, 30, 20, 0);
        Assert.Equal(1, b.Level);
        Assert.Equal(UpgradeCheck.BaseLevelTooLow, w.CheckUpgradeBuilding(b));

        w.Enqueue(new UpgradeBaseCommand(), 0); // the base rises to level 2 first
        Run(w, 41);
        Assert.Equal(UpgradeCheck.Ok, w.CheckUpgradeBuilding(b));
        int before = w.Resources;
        w.Enqueue(new UpgradeBuildingCommand(b.Id), 0);
        w.Step();
        Assert.True(b.Upgrading);
        Assert.Equal(before - Defs.Building(BuildingKind.Barracks).UpgradeCost[0], w.Resources);
        Assert.Equal(UpgradeCheck.AlreadyUpgrading, w.CheckUpgradeBuilding(b));
        Run(w, 31);
        Assert.Equal(2, b.Level);
        Assert.False(b.Upgrading);
    }

    [Fact]
    public void Factory_MaxLevelIs3_AndOtherBuildingsCannotBeRaised()
    {
        var w = NewBase();
        w.SetBaseLevel(0, 5);
        Building b = w.PlaceBuilding(BuildingKind.VehicleFactory, 30, 20, 0);
        // a ready building of a scenario matches the base, up to its own maximum
        Assert.Equal(3, b.Level);
        Assert.Equal(UpgradeCheck.MaxLevel, w.CheckUpgradeBuilding(b));
        Building tower = w.PlaceBuilding(BuildingKind.Tower, 40, 20, 0);
        Assert.Equal(UpgradeCheck.NotAllowed, w.CheckUpgradeBuilding(tower));
        Assert.Equal(5, Economy.MaxBaseLevel);
    }

    [Fact]
    public void BaseLevel_RaisesEnergyAndIncome()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10, 0);
        Assert.Equal(Economy.HqEnergyAt(1), w.EnergyCapacity);
        Assert.Equal(Economy.HqIncomeAt(1), w.IncomePerMinute);
        w.SetBaseLevel(0, 3);
        Assert.Equal(Economy.HqEnergyAt(3), w.EnergyCapacity);
        Assert.Equal(Economy.HqIncomeAt(3), w.IncomePerMinute);
        Assert.True(Economy.HqEnergyAt(3) > Economy.HqEnergyAt(1));
        Assert.True(Economy.HqIncomeAt(3) > Economy.HqIncomeAt(1));
    }

    [Fact]
    public void BaseUpgrade_Finishing_RaisesIncome()
    {
        var w = NewBase();
        w.Enqueue(new UpgradeBaseCommand(), 0);
        Run(w, 41);
        Assert.Equal(2, w.Player(0).BaseLevel);
        Assert.Equal(Economy.HqIncomeAt(2), w.IncomePerMinute);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NewUnits_AppearAtTheDoorSide(int facing)
    {
        var w = NewBase();
        Building b = w.PlaceBuilding(BuildingKind.Barracks, 30, 30, 0, facing: facing);
        Assert.Equal(facing, b.Facing);
        w.Enqueue(new QueueUnitCommand(b.Id, UnitKind.Rifleman), 0);
        Run(w, 8);
        Unit u = null;
        foreach (Unit x in w.Units) if (x.Team == 0) u = x;
        Assert.NotNull(u);
        int ux = u.Pos.X.ToInt(), uy = u.Pos.Y.ToInt();
        switch (facing)
        {
            case 0: Assert.True(uy < b.Y); break;
            case 1: Assert.True(ux >= b.X + b.Width); break;
            case 2: Assert.True(uy >= b.Y + b.Height); break;
            case 3: Assert.True(ux < b.X); break;
        }
    }

    [Fact]
    public void TurnedSideways_TheFootprintSwapsWidthAndHeight()
    {
        var w = NewBase();
        BuildingDef def = Defs.Building(BuildingKind.Barracks);
        Assert.NotEqual(def.Width, def.Height);
        Building b = w.PlaceBuilding(BuildingKind.Barracks, 30, 30, 0, facing: 1);
        Assert.Equal(def.Height, b.Width);
        Assert.Equal(def.Width, b.Height);
        // the cells really are taken
        Assert.Equal(BuildCheck.BadGround, w.CheckBuild(BuildingKind.Barracks, 30 + b.Width - 1, 30 + b.Height - 1, 0));
        // the same spot is free for a building turned the other way when it fits beside
        Assert.Equal(BuildCheck.Ok, w.CheckBuild(BuildingKind.Barracks, 30 + b.Width, 30, 0, 1));
    }

    [Fact]
    public void BuildCommand_CarriesTheDoorSide()
    {
        var w = NewBase();
        w.Enqueue(new BuildCommand(BuildingKind.Barracks, 30, 30, 3), 0);
        w.Step();
        Building b = null;
        foreach (Building x in w.Buildings) if (x.Kind == BuildingKind.Barracks) b = x;
        Assert.NotNull(b);
        Assert.Equal(3, b.Facing);

        // no side given: the side towards the middle of the map
        w.Enqueue(new BuildCommand(BuildingKind.Barracks, 36, 30), 0);
        w.Step();
        Building c = null;
        foreach (Building x in w.Buildings) if (x.Kind == BuildingKind.Barracks && x.Id != b.Id) c = x;
        Assert.Equal(0, c.Facing);
    }

    [Fact]
    public void HqRally_SetsEveryProductionBuilding_AndLaterOnes()
    {
        var w = NewBase();
        Building hq = null;
        foreach (Building x in w.Buildings) if (x.Kind == BuildingKind.Hq && x.Team == 0) hq = x;
        Building b1 = w.PlaceBuilding(BuildingKind.Barracks, 30, 30, 0);
        Building b2 = w.PlaceBuilding(BuildingKind.VehicleFactory, 36, 30, 0);
        Building power = null;
        foreach (Building x in w.Buildings) if (x.Kind == BuildingKind.PowerPlant) power = x;

        w.Enqueue(new SetRallyCommand(hq.Id, P(40, 40)), 0);
        w.Step();
        Assert.True(b1.HasRally); Assert.True(b2.HasRally);
        Assert.Equal(P(40, 40), b1.Rally);
        Assert.Equal(P(40, 40), b2.Rally);
        Assert.False(power.HasRally);

        Building b3 = w.PlaceBuilding(BuildingKind.Barracks, 30, 36, 0);
        Assert.True(b3.HasRally);
        Assert.Equal(P(40, 40), b3.Rally);

        // a single building can be changed afterwards
        w.Enqueue(new SetRallyCommand(b1.Id, P(20, 40)), 0);
        w.Step();
        Assert.Equal(P(20, 40), b1.Rally);
        Assert.Equal(P(40, 40), b2.Rally);
    }

    [Fact]
    public void QueueAnywhere_SpreadsTheLoad()
    {
        var w = NewBase();
        Building a = w.PlaceBuilding(BuildingKind.VehicleFactory, 30, 30, 0);
        Building b = w.PlaceBuilding(BuildingKind.VehicleFactory, 36, 30, 0);
        Building c = w.PlaceBuilding(BuildingKind.VehicleFactory, 42, 30, 0);
        w.SetBaseLevel(0, 5);

        // a and b already hold work, c is idle
        for (int i = 0; i < 3; i++) { w.Enqueue(new QueueUnitCommand(a.Id, UnitKind.Tank), 0); w.Enqueue(new QueueUnitCommand(b.Id, UnitKind.Tank), 0); }
        w.Step();
        Assert.Equal(3, a.Queue.Count); Assert.Equal(3, b.Queue.Count); Assert.Empty(c.Queue);

        for (int i = 0; i < 4; i++) w.Enqueue(new QueueUnitCommand(QueueUnitCommand.AnyBuilding, UnitKind.Tank), 0);
        w.Step();
        // the idle one takes three, then all are even and the fourth goes to the lowest id
        Assert.Equal(3, c.Queue.Count);
        Assert.Equal(4, a.Queue.Count);
        Assert.Equal(3, b.Queue.Count);
    }

    [Fact]
    public void QueueAnywhere_ReportsWhyItCannot()
    {
        var w = NewBase();
        Assert.Equal(QueueCheck.NoBuilding, w.CheckQueueAny(0, UnitKind.Rifleman));
        Building barracks = w.PlaceBuilding(BuildingKind.Barracks, 30, 30, 0);
        Assert.Equal(QueueCheck.Ok, w.CheckQueueAny(0, UnitKind.Rifleman));
        Assert.Equal(QueueCheck.LevelTooLow, w.CheckQueueAny(0, UnitKind.RocketTrooper));
        w.Enqueue(new UpgradeBaseCommand(), 0); // the base rises, the barracks stays behind
        Run(w, 41);
        Assert.Equal(1, barracks.Level);
        Assert.Equal(QueueCheck.BuildingLevelTooLow, w.CheckQueueAny(0, UnitKind.RocketTrooper));
        Assert.Equal(QueueCheck.NoBuilding, w.CheckQueueAny(0, UnitKind.Jeep));

        w.GiveResources(0, -w.Resources);
        Assert.Equal(QueueCheck.NotEnoughResources, w.CheckQueueAny(0, UnitKind.Rifleman));
    }

    [Fact]
    public void UnderConstruction_FactoryIsNotAProducer()
    {
        var w = NewBase();
        w.Enqueue(new BuildCommand(BuildingKind.Barracks, 30, 30), 0);
        w.Step();
        Assert.Equal(QueueCheck.NoBuilding, w.CheckQueueAny(0, UnitKind.Rifleman));
    }

    [Fact]
    public void NewCommands_SurviveTheCodec()
    {
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new UpgradeBuildingCommand(7)), out Command c));
        Assert.Equal(7, ((UpgradeBuildingCommand)c).BuildingId);
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new BuildCommand(BuildingKind.Barracks, 3, 4, 2)), out Command d));
        Assert.Equal(2, ((BuildCommand)d).Facing);
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new BuildCommand(BuildingKind.Barracks, 3, 4)), out Command e));
        Assert.Equal(-1, ((BuildCommand)e).Facing);
        Assert.False(CommandCodec.TryDecode(new byte[] { 2, 3, 1, 0, 0, 0, 2, 1, 0, 0, 9 }, out _)); // bad door side
    }
}
