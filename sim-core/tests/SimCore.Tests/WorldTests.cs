using SimCore;

namespace SimCore.Tests;

public class WorldTests
{
    private static World NewWorld() => new World(Fix64.FromInt(64), Fix64.FromInt(64));
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

    [Fact]
    public void Spawn_AssignsIncreasingIds_AndClampsIntoMap()
    {
        World w = NewWorld();
        Unit a = w.SpawnUnit(P(5, 5), Fix64.FromInt(2));
        Unit b = w.SpawnUnit(P(-10, 500), Fix64.FromInt(2));

        Assert.Equal(1, a.Id);
        Assert.Equal(2, b.Id);
        Assert.Equal(Fix64.Half, b.Pos.X);
        Assert.Equal(Fix64.FromInt(64) - Fix64.Half, b.Pos.Y);
    }

    [Fact]
    public void Unit_WalksToTarget_AtItsSpeed()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(P(10, 10), Fix64.FromInt(2)); // 2 map units per second
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(20, 10)));

        for (int i = 0; i < 99; i++) w.Step();
        Assert.True(u.Moving);                  // 10 units at 2/s takes ~5s = ~100 ticks
        Assert.True(u.Pos.X < Fix64.FromInt(20));

        for (int i = 0; i < 3; i++) w.Step();
        Assert.False(u.Moving);
        Assert.Equal(P(20, 10), u.Pos);
    }

    [Fact]
    public void Unit_WalksDiagonally_AndArrivesExactly()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(P(10, 10), Fix64.FromInt(3));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(25, 30)));

        w.Step(); // the command is applied during the first step
        Assert.True(u.Moving);
        for (int i = 0; i < 400 && u.Moving; i++) w.Step();

        Assert.False(u.Moving);
        Assert.Equal(P(25, 30), u.Pos);
    }

    [Fact]
    public void PrevPos_IsPositionAtStartOfTick()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(P(10, 10), Fix64.FromInt(2));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(20, 10)));

        w.Step();
        Vec2 afterFirst = u.Pos;
        w.Step();

        Assert.Equal(afterFirst, u.PrevPos);
        Assert.True(u.Pos.X > u.PrevPos.X);
    }

    [Fact]
    public void Formation_GivesEveryUnitItsOwnSlot_CentredOnTarget()
    {
        Vec2[] offsets = World.FormationOffsets(9);
        Assert.Equal(9, offsets.Length);
        Assert.Equal(9, offsets.Select(o => o.ToString()).Distinct().Count());

        Fix64 sumX = Fix64.Zero, sumY = Fix64.Zero;
        foreach (Vec2 o in offsets) { sumX += o.X; sumY += o.Y; }
        Assert.Equal(Fix64.Zero, sumX);
        Assert.Equal(Fix64.Zero, sumY);
    }

    [Fact]
    public void GroupMove_SpreadsUnitsAroundTarget()
    {
        World w = NewWorld();
        var ids = new List<int>();
        for (int i = 0; i < 6; i++) ids.Add(w.SpawnUnit(P(10 + i, 10), Fix64.FromInt(4)).Id);
        w.Enqueue(new MoveCommand(ids.ToArray(), P(40, 40)));

        for (int i = 0; i < 600; i++) w.Step();

        var finalPositions = ids.Select(id => w.GetUnit(id).Pos.ToString()).ToList();
        Assert.Equal(6, finalPositions.Distinct().Count());
        Assert.All(ids, id => Assert.False(w.GetUnit(id).Moving));
    }

    [Fact]
    public void Target_OutsideMap_IsClamped()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(P(10, 10), Fix64.FromInt(10));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(1000, -1000)));

        w.Step(); // the command is applied during the first step
        Assert.True(u.Moving);
        for (int i = 0; i < 1000 && u.Moving; i++) w.Step();

        Assert.Equal(Fix64.FromInt(64) - Fix64.Half, u.Pos.X);
        Assert.Equal(Fix64.Half, u.Pos.Y);
    }

    [Fact]
    public void UnknownUnitIds_AreIgnored()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(P(10, 10), Fix64.FromInt(2));
        w.Enqueue(new MoveCommand(new[] { 999, u.Id }, P(15, 10)));

        w.Step();

        Assert.True(u.Moving);
    }

    [Fact]
    public void SameCommands_GiveSameStateHash_OnTwoWorlds()
    {
        ulong Run(int targetX)
        {
            World w = NewWorld();
            var ids = new List<int>();
            for (int i = 0; i < 12; i++) ids.Add(w.SpawnUnit(P(20 + i % 4, 20 + i / 4), Fix64.FromInt(3)).Id);
            w.Enqueue(new MoveCommand(ids.ToArray(), P(targetX, 45)));
            for (int i = 0; i < 500; i++) w.Step();
            w.Enqueue(new MoveCommand(ids.Take(5).ToArray(), P(10, 10)));
            for (int i = 0; i < 500; i++) w.Step();
            return w.StateHash();
        }

        Assert.Equal(Run(50), Run(50));
        Assert.NotEqual(Run(50), Run(51));
    }
}
