using SimCore;

namespace SimCore.Tests;

public class TwoPlayerTests
{
    private const int A = World.TeamPlayer, B = World.TeamEnemy;

    /// <summary>A map with two bases far apart: team A bottom-left, team B top-right.</summary>
    private static (World w, Building hqA, Building rxA, Building hqB, Building rxB) NewMatch()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        Building hqA = w.PlaceBuilding(BuildingKind.Hq, 8, 8, A);
        Building pA = w.PlaceBuilding(BuildingKind.PowerPlant, 16, 8, A);
        Building rxA = w.PlaceBuilding(BuildingKind.Barracks, 8, 16, A);
        Building hqB = w.PlaceBuilding(BuildingKind.Hq, 50, 50, B);
        Building pB = w.PlaceBuilding(BuildingKind.PowerPlant, 44, 50, B);
        Building rxB = w.PlaceBuilding(BuildingKind.Barracks, 50, 44, B);
        return (w, hqA, rxA, hqB, rxB);
    }

    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    [Fact]
    public void EachTeam_HasItsOwnResourcesEnergyAndIncome()
    {
        var (w, _, _, _, _) = NewMatch();
        Assert.Equal(Economy.StartResources, w.Player(A).Resources);
        Assert.Equal(Economy.StartResources, w.Player(B).Resources);
        Assert.Equal(Economy.HqEnergy + 120, w.Player(A).EnergyCapacity);
        Assert.Equal(Economy.HqEnergy + 120, w.Player(B).EnergyCapacity);
        Assert.Equal(30, w.Player(A).EnergyUsed);
        Assert.Equal(30, w.Player(B).EnergyUsed);
        Assert.Equal(Economy.HqIncomePerMinute, w.Player(A).IncomePerMinute);
    }

    [Fact]
    public void Spending_OnlyAffectsTheSpender()
    {
        var (w, _, rxA, _, rxB) = NewMatch();
        w.Enqueue(new QueueUnitCommand(rxA.Id, UnitKind.Rifleman), A);
        w.Step();
        Assert.Equal(Economy.StartResources - 50, w.Player(A).Resources);
        Assert.Equal(Economy.StartResources, w.Player(B).Resources);

        w.Enqueue(new QueueUnitCommand(rxB.Id, UnitKind.RocketTrooper), B);
        w.Step();
        Assert.Equal(Economy.StartResources - 50, w.Player(A).Resources);
        Assert.Equal(Economy.StartResources - 80, w.Player(B).Resources);
    }

    [Fact]
    public void BothTeams_ProduceUnitsOfTheirOwnTeam()
    {
        var (w, _, rxA, _, rxB) = NewMatch();
        w.Enqueue(new QueueUnitCommand(rxA.Id, UnitKind.Rifleman), A);
        w.Enqueue(new QueueUnitCommand(rxB.Id, UnitKind.Rifleman), B);
        Run(w, 6);
        Assert.Equal(2, w.Units.Count);
        Assert.Contains(w.Units, u => u.Team == A && u.Pos.Y.ToFloat() < 30f);
        Assert.Contains(w.Units, u => u.Team == B && u.Pos.Y.ToFloat() > 30f);
    }

    [Fact]
    public void BuildCommand_PlacesTheBuildingForTheSender_AndChargesTheSender()
    {
        var (w, _, _, _, _) = NewMatch();
        w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 40, 40), B);
        w.Step();
        Building built = w.Buildings[w.Buildings.Count - 1];
        Assert.Equal(B, built.Team);
        Assert.Equal(Economy.StartResources - 100, w.Player(B).Resources);
        Assert.Equal(Economy.StartResources, w.Player(A).Resources);
        Assert.Equal(Economy.HqEnergy + 120 + 80, w.Player(B).EnergyCapacity); // second power plant gives 80
        Assert.Equal(Economy.HqEnergy + 120, w.Player(A).EnergyCapacity);
    }

    [Fact]
    public void EnergyShortage_OfOneTeam_DoesNotStopTheOther()
    {
        var (w, _, rxA, _, rxB) = NewMatch();
        // Team A builds supply centers until it is short of energy (each needs 20): 140 capacity, barracks uses 30.
        for (int i = 0; i < 6; i++) w.PlaceBuilding(BuildingKind.SupplyCenter, 20 + i * 4, 30, A);
        Assert.True(w.Player(A).EnergyShortage);
        Assert.False(w.Player(B).EnergyShortage);
        Assert.False(rxA.Powered);
        Assert.True(rxB.Powered);
    }

    [Fact]
    public void Income_ArrivesForBothTeams()
    {
        var (w, _, _, _, _) = NewMatch();
        Run(w, 60);
        Assert.Equal(Economy.StartResources + Economy.HqIncomePerMinute, w.Player(A).Resources);
        Assert.Equal(Economy.StartResources + Economy.HqIncomePerMinute, w.Player(B).Resources);
    }

    [Fact]
    public void Orders_ForSomeoneElsesUnits_AreIgnored()
    {
        var (w, _, _, _, _) = NewMatch();
        Unit mine = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(20), Fix64.FromInt(30)), A);
        Unit theirs = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(40), Fix64.FromInt(30)), B);

        // B tries to order A's unit around.
        w.Enqueue(new MoveCommand(new[] { mine.Id }, new Vec2(Fix64.FromInt(30), Fix64.FromInt(30))), B);
        w.Step();
        Assert.False(mine.Moving);

        // B orders its own unit: works.
        w.Enqueue(new MoveCommand(new[] { theirs.Id }, new Vec2(Fix64.FromInt(30), Fix64.FromInt(30))), B);
        w.Step();
        Assert.True(theirs.Moving);
    }

    [Fact]
    public void Attack_OnYourOwnUnit_IsIgnored_AndOnTheEnemy_Works()
    {
        var (w, _, _, _, _) = NewMatch();
        Unit a1 = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(20), Fix64.FromInt(30)), A);
        Unit a2 = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(22), Fix64.FromInt(30)), A);
        Unit b1 = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(40), Fix64.FromInt(30)), B);

        w.Enqueue(new AttackCommand(new[] { a1.Id }, AttackKind.Unit, a2.Id), A); // friendly fire target
        w.Step();
        Assert.Equal(AttackKind.None, a1.AttackKind);

        w.Enqueue(new AttackCommand(new[] { a1.Id }, AttackKind.Unit, b1.Id), A);
        w.Step();
        Assert.Equal(AttackKind.Unit, a1.AttackKind);
        Assert.Equal(b1.Id, a1.AttackId);
    }

    [Fact]
    public void QueueRallyCancel_OnSomeoneElsesBuilding_AreIgnored()
    {
        var (w, _, rxA, _, _) = NewMatch();
        w.Enqueue(new QueueUnitCommand(rxA.Id, UnitKind.Rifleman), B);
        w.Enqueue(new SetRallyCommand(rxA.Id, new Vec2(Fix64.FromInt(5), Fix64.FromInt(5))), B);
        w.Step();
        Assert.Empty(rxA.Queue);
        Assert.False(rxA.HasRally);
        Assert.Equal(Economy.StartResources, w.Player(B).Resources);

        w.Enqueue(new QueueUnitCommand(rxA.Id, UnitKind.Rifleman), A);
        w.Step();
        w.Enqueue(new CancelQueuedCommand(rxA.Id, 0), B);
        w.Step();
        Assert.Single(rxA.Queue);
    }

    [Fact]
    public void InvalidTeamNumber_DoesNothingAndNeverThrows()
    {
        var (w, _, rxA, _, _) = NewMatch();
        foreach (int team in new[] { -1, 2, 99 })
        {
            w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 30, 30), team);
            w.Enqueue(new QueueUnitCommand(rxA.Id, UnitKind.Rifleman), team);
            w.Enqueue(new MoveCommand(new int[0], new Vec2(Fix64.FromInt(1), Fix64.FromInt(1))), team);
            w.Enqueue(new AttackCommand(new int[0], AttackKind.Unit, 1), team);
        }
        w.Step();
        Assert.Equal(6, w.Buildings.Count);
        Assert.Empty(rxA.Queue);
    }

    [Fact]
    public void SameCommands_GiveTheSameHash_OnTwoSeparateWorlds()
    {
        ulong Play()
        {
            var (w, _, rxA, _, rxB) = NewMatch();
            for (int tick = 0; tick < 600; tick++)
            {
                if (tick == 0) w.Enqueue(new QueueUnitCommand(rxA.Id, UnitKind.Rifleman), A);
                if (tick == 5) w.Enqueue(new QueueUnitCommand(rxB.Id, UnitKind.RocketTrooper), B);
                if (tick == 100) w.Enqueue(new BuildCommand(BuildingKind.SupplyCenter, 30, 30), A);
                if (tick == 200) w.Enqueue(new BuildCommand(BuildingKind.PowerPlant, 40, 40), B);
                if (tick == 250)
                {
                    foreach (Unit u in w.Units)
                        w.Enqueue(new MoveCommand(new[] { u.Id }, new Vec2(Fix64.FromInt(32), Fix64.FromInt(32))), u.Team);
                }
                w.Step();
            }
            return w.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Hash_ChangesWhenOnlyTheSecondTeamSpends()
    {
        var (w1, _, _, _, rxB1) = NewMatch();
        var (w2, _, _, _, _) = NewMatch();
        w1.Enqueue(new QueueUnitCommand(rxB1.Id, UnitKind.Rifleman), B);
        w1.Step(); w2.Step();
        Assert.NotEqual(w1.StateHash(), w2.StateHash());
    }
}
