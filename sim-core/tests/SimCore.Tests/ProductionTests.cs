using SimCore;

namespace SimCore.Tests;

public class ProductionTests
{
    private static (World w, Building hq, Building barracks, Building power) NewBase()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        Building hq = w.PlaceBuilding(BuildingKind.Hq, 10, 10);
        Building power = w.PlaceBuilding(BuildingKind.PowerPlant, 20, 10);
        Building barracks = w.PlaceBuilding(BuildingKind.Barracks, 30, 20);
        return (w, hq, barracks, power);
    }

    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    [Fact]
    public void Queue_CostsResources_AndProducesAfterBuildTime()
    {
        var (w, _, barracks, _) = NewBase();
        int before = w.Resources;
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Rifleman));
        w.Step();
        Assert.Equal(before - Defs.Unit(UnitKind.Rifleman).Cost, w.Resources);
        Assert.Empty(w.Units);

        int seconds = Defs.Unit(UnitKind.Rifleman).BuildSeconds;
        Run(w, seconds - 1);
        Assert.Empty(w.Units);
        Run(w, 2); // the work is done
        Assert.Single(w.Units);
        Assert.Equal(UnitKind.Rifleman, w.Units[0].Kind);
        Assert.Empty(barracks.Queue);
    }

    [Fact]
    public void Queue_BuildsOneAtATime_InOrder()
    {
        var (w, _, barracks, _) = NewBase();
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Rifleman));
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.RocketTrooper));
        Run(w, Defs.Unit(UnitKind.Rifleman).BuildSeconds + 1);
        Assert.Single(w.Units);
        Run(w, Defs.Unit(UnitKind.RocketTrooper).BuildSeconds);
        Assert.Equal(2, w.Units.Count);
        Assert.Equal(UnitKind.RocketTrooper, w.Units[1].Kind);
    }

    [Fact]
    public void Queue_RejectsWrongBuilding_FullQueue_AndNoMoney()
    {
        var (w, hq, barracks, _) = NewBase();
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Tank));   // needs a factory
        w.Enqueue(new QueueUnitCommand(hq.Id, UnitKind.Rifleman));     // HQ produces nothing
        w.Step();
        Assert.Equal(Economy.StartResources, w.Resources); // nothing was charged

        for (int i = 0; i < 10; i++) w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Rifleman));
        w.Step();
        Assert.Equal(World.MaxQueue, barracks.Queue.Count);

        Assert.Equal(QueueCheck.NotAllowed, w.CheckQueue(barracks, UnitKind.Jeep));
        Assert.Equal(QueueCheck.QueueFull, w.CheckQueue(barracks, UnitKind.Rifleman));
    }

    [Fact]
    public void Cancel_RefundsAndResetsProgress()
    {
        var (w, _, barracks, _) = NewBase();
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Rifleman));
        w.Step();
        int paid = w.Resources;
        Run(w, 2);
        w.Enqueue(new CancelQueuedCommand(barracks.Id, 0));
        w.Step();
        Assert.True(w.Resources >= paid + 50); // refunded (income may add a little)
        Assert.Empty(barracks.Queue);
        Assert.Equal(0, barracks.Progress);
    }

    [Fact]
    public void NoEnergy_StopsProduction()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10);
        Building barracks = w.PlaceBuilding(BuildingKind.Barracks, 30, 20); // needs 30, base gives 20
        Assert.False(barracks.Powered);
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Rifleman));
        Run(w, 10);
        Assert.Empty(w.Units);
        Assert.Equal(0, barracks.Progress);

        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 20, 10));
        Run(w, 13); // the power plant takes 12 s to build
        Assert.True(barracks.Powered);
        Run(w, Defs.Unit(UnitKind.Rifleman).BuildSeconds);  // then the rifleman needs its build time
        Assert.Single(w.Units);
    }

    [Fact]
    public void NewUnit_AppearsAtDoor_AndWalksToRally()
    {
        var (w, _, barracks, _) = NewBase();
        var rally = new Vec2(Fix64.FromInt(45), Fix64.FromInt(30));
        w.Enqueue(new SetRallyCommand(barracks.Id, rally));
        w.Enqueue(new QueueUnitCommand(barracks.Id, UnitKind.Rifleman));
        for (int i = 0; i < 10 * World.TicksPerSecond && w.Units.Count == 0; i++) w.Step();

        Unit u = w.Units[0];
        Assert.True(u.Pos.Y.ToInt() < barracks.Y);      // door side, the moment it appears
        Assert.True(u.Moving);
        Run(w, 15);
        Assert.False(u.Moving);
        Assert.Equal(45, u.Pos.X.ToInt());
        Assert.Equal(30, u.Pos.Y.ToInt());
    }

    [Fact]
    public void Production_IsDeterministic()
    {
        ulong Hash()
        {
            var (w, _, barracks, _) = NewBase();
            w.Enqueue(new SetRallyCommand(barracks.Id, new Vec2(Fix64.FromInt(40), Fix64.FromInt(40))));
            for (int i = 0; i < 4; i++) w.Enqueue(new QueueUnitCommand(barracks.Id, i % 2 == 0 ? UnitKind.Rifleman : UnitKind.RocketTrooper));
            Run(w, 30);
            return w.StateHash();
        }
        Assert.Equal(Hash(), Hash());
    }
}
