using SimCore;

namespace SimCore.Tests;

public class OrderTests
{
    private static World NewWorld() => new World(Fix64.FromInt(64), Fix64.FromInt(64));
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    [Fact]
    public void Stop_HaltsAMovingUnit()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(10, 50)));
        Run(w, 2);
        Assert.True(u.Moving);
        w.Enqueue(new StopCommand(new[] { u.Id }));
        w.Step();
        Assert.False(u.Moving);
        Vec2 at = u.Pos;
        Run(w, 2);
        Assert.Equal(at, u.Pos);
    }

    [Fact]
    public void Stop_DropsAnAttackOrder()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(10, 45), World.TeamEnemy);
        w.Enqueue(new AttackCommand(new[] { u.Id }, AttackKind.Unit, enemy.Id));
        Run(w, 2);
        w.Enqueue(new StopCommand(new[] { u.Id }));
        w.Step();
        Assert.Equal(AttackKind.None, u.AttackKind);
        Assert.False(u.Moving);
    }

    [Fact]
    public void OtherTeamsUnits_IgnoreStop()
    {
        World w = NewWorld();
        Unit enemy = w.SpawnUnit(UnitKind.Rifleman, P(10, 10), World.TeamEnemy);
        w.Enqueue(new MoveCommand(new[] { enemy.Id }, P(10, 50)), World.TeamEnemy);
        Run(w, 1);
        w.Enqueue(new StopCommand(new[] { enemy.Id }), 0);
        Run(w, 1);
        Assert.True(enemy.Moving);
    }

    [Fact]
    public void Hold_ShootsInRange_ButNeverChases()
    {
        World w = NewWorld();
        Unit holder = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        Unit far = w.SpawnUnit(UnitKind.Rifleman, P(10, 10 + 8), World.TeamEnemy); // in sight, out of weapon range
        w.Enqueue(new HoldCommand(new[] { holder.Id }));
        w.Enqueue(new HoldCommand(new[] { far.Id }), World.TeamEnemy); // the other side stands still as well
        Vec2 start = holder.Pos;
        Run(w, 4);
        Assert.True(holder.Hold);
        Assert.Equal(start, holder.Pos);
        Assert.Equal(AttackKind.None, holder.AttackKind);

        // an enemy that walks into range is shot
        Unit near = w.SpawnUnit(UnitKind.Tank, P(10, 10 + 3), World.TeamEnemy);
        Run(w, 3);
        Assert.True(near.Hp < near.MaxHp);
        Assert.Equal(start, holder.Pos);
    }

    [Fact]
    public void Idle_UnitsChaseButHoldDoesNot_Contrast()
    {
        World w = NewWorld();
        Unit idle = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        w.SpawnUnit(UnitKind.Rifleman, P(10, 18), World.TeamEnemy);
        Run(w, 3);
        Assert.True(idle.Pos.Y.ToInt() > 10, "an idle unit walks towards what it sees");
    }

    [Fact]
    public void MoveOrder_ClearsHold()
    {
        World w = NewWorld();
        Unit u = w.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        w.Enqueue(new HoldCommand(new[] { u.Id }));
        w.Step();
        Assert.True(u.Hold);
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(20, 10)));
        w.Step();
        Assert.False(u.Hold);
    }

    [Fact]
    public void AttackMove_FightsOnTheWay_ThenReachesTheGoal()
    {
        World w = NewWorld();
        Unit tank = w.SpawnUnit(UnitKind.Tank, P(10, 10));
        Unit enemy = w.SpawnUnit(UnitKind.Rifleman, P(12, 25), World.TeamEnemy); // beside the route
        w.Enqueue(new AttackMoveCommand(new[] { tank.Id }, P(10, 45)));
        Run(w, 4);
        Assert.True(tank.AttackMoving);
        Run(w, 30);
        Assert.Null(w.GetUnit(enemy.Id));
        Assert.False(tank.AttackMoving, "it finished the walk");
        Assert.True((tank.Pos - P(10, 45)).SqrLength < Fix64.FromInt(9), "and stands at the goal");
    }

    [Fact]
    public void NewCommands_RoundTripThroughTheCodec()
    {
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new StopCommand(new[] { 1, 2 })), out Command c));
        Assert.Equal(new[] { 1, 2 }, ((StopCommand)c).UnitIds);
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new HoldCommand(new[] { 7 })), out c));
        Assert.Equal(new[] { 7 }, ((HoldCommand)c).UnitIds);
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(new AttackMoveCommand(new[] { 3 }, P(5, 6))), out c));
        var am = (AttackMoveCommand)c;
        Assert.Equal(new[] { 3 }, am.UnitIds);
        Assert.Equal(P(5, 6), am.Target);
    }

    [Fact]
    public void StateHash_CoversHoldAndAttackMove()
    {
        World a = NewWorld(), b = NewWorld();
        Unit ua = a.SpawnUnit(UnitKind.Rifleman, P(10, 10)), ub = b.SpawnUnit(UnitKind.Rifleman, P(10, 10));
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Enqueue(new HoldCommand(new[] { ua.Id }));
        a.Step(); b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void MixedOrders_PlayTheSameInTwoWorlds()
    {
        ulong Play()
        {
            World w = NewWorld();
            var mine = new int[6];
            for (int i = 0; i < 6; i++) mine[i] = w.SpawnUnit(i % 2 == 0 ? UnitKind.Rifleman : UnitKind.Tank, P(10 + i, 10)).Id;
            var theirs = new int[4];
            for (int i = 0; i < 4; i++) theirs[i] = w.SpawnUnit(UnitKind.Rifleman, P(14 + i, 36), World.TeamEnemy).Id;
            ulong hash = 0;
            for (int tick = 0; tick < 40 * World.TicksPerSecond; tick++)
            {
                if (tick == 5) w.Enqueue(new AttackMoveCommand(mine, P(14, 40)));
                if (tick == 200) w.Enqueue(new HoldCommand(new[] { mine[0], mine[1] }));
                if (tick == 300) w.Enqueue(new StopCommand(new[] { mine[2] }));
                if (tick == 400) w.Enqueue(new MoveCommand(new[] { mine[0] }, P(20, 20)));
                if (tick == 10) w.Enqueue(new HoldCommand(theirs), World.TeamEnemy);
                w.Step();
                if (tick % 50 == 0) hash = hash * 31 + w.StateHash();
            }
            return hash;
        }
        Assert.Equal(Play(), Play());
    }
}
