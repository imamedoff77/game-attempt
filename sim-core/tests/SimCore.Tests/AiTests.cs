using SimCore;
using SimCore.Tests;
using Xunit;

public class AiTests
{
    /// <summary>Wins of <paramref name="a"/> over <paramref name="b"/> in <paramref name="games"/> matches, each pairing played from both sides.</summary>
    private static int WinsOf(AiDifficulty a, AiDifficulty b, int games)
    {
        int wins = 0;
        for (int i = 1; i <= games; i++)
        {
            bool swap = i % 2 == 0;
            AiMatch.Result r = swap ? AiMatch.Run(b, a, (ulong)i * 7919) : AiMatch.Run(a, b, (ulong)i * 7919);
            if (r.Winner >= 0 && (r.Winner == 0) != swap) wins++;
        }
        return wins;
    }

    [Theory]
    [InlineData(AiDifficulty.Easy, AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Easy, AiDifficulty.Hard)]
    [InlineData(AiDifficulty.Medium, AiDifficulty.Medium)]
    [InlineData(AiDifficulty.Medium, AiDifficulty.Hard)]
    [InlineData(AiDifficulty.Hard, AiDifficulty.Hard)]
    public void Matches_NeverStall_SomeoneWinsWithinAnHour(AiDifficulty a, AiDifficulty b)
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            AiMatch.Result r = AiMatch.Run(a, b, seed * 104729, 60);
            Assert.True(r.Winner >= 0, $"{a} vs {b}, seed {seed}: no winner in an hour (units {r.Units0}/{r.Units1})");
        }
    }

    [Fact]
    public void HardBeatsEasy_AlmostAlways()
    {
        Assert.True(WinsOf(AiDifficulty.Hard, AiDifficulty.Easy, 12) >= 11);
    }

    [Fact]
    public void MediumBeatsEasy_AlmostAlways()
    {
        Assert.True(WinsOf(AiDifficulty.Medium, AiDifficulty.Easy, 12) >= 9);
    }

    [Fact]
    public void HardBeatsMedium_MoreOftenThanNot()
    {
        Assert.True(WinsOf(AiDifficulty.Hard, AiDifficulty.Medium, 16) >= 9);
    }

    [Fact]
    public void SameSeed_GivesTheSameMatch_DifferentSeedsDiffer()
    {
        AiMatch.Result a = AiMatch.Run(AiDifficulty.Medium, AiDifficulty.Hard, 99);
        AiMatch.Result b = AiMatch.Run(AiDifficulty.Medium, AiDifficulty.Hard, 99);
        AiMatch.Result c = AiMatch.Run(AiDifficulty.Medium, AiDifficulty.Hard, 100);
        Assert.Equal(a, b);
        Assert.NotEqual(a.Hash, c.Hash);
    }

    [Fact]
    public void EveryCommand_IsOrdinary_AndSurvivesTheNetworkCodec()
    {
        World w = SkirmishScenario.Create(5);
        var ai = new AiPlayer(0, AiDifficulty.Hard, 5);
        var enemy = new AiPlayer(1, AiDifficulty.Medium, 6);
        var buffer = new List<Command>();
        int total = 0;
        for (int t = 0; t < 20 * 60 * 4; t++)
        {
            buffer.Clear();
            ai.Update(w, buffer);
            foreach (Command c in buffer)
            {
                byte[] bytes = CommandCodec.Encode(c);
                Assert.True(CommandCodec.TryDecode(bytes, out Command back));
                Assert.Equal(c.GetType(), back.GetType());
                w.Enqueue(c, 0);
                total++;
            }
            buffer.Clear();
            enemy.Update(w, buffer);
            foreach (Command c in buffer) w.Enqueue(c, 1);
            w.Step();
        }
        Assert.True(total > 15, "the AI hardly did anything: " + total);
    }

    [Fact]
    public void Orders_AreLegalWhenGiven_AndMoneyNeverGoesNegative()
    {
        World w = SkirmishScenario.Create(2);
        var ais = new[] { new AiPlayer(0, AiDifficulty.Hard, 11), new AiPlayer(1, AiDifficulty.Hard, 12) };
        var buffer = new List<Command>();
        int checkedOrders = 0, legal = 0;

        for (int t = 0; t < 20 * 60 * 6 && w.HasBuilding(BuildingKind.Hq, 0) && w.HasBuilding(BuildingKind.Hq, 1); t++)
        {
            for (int team = 0; team < 2; team++)
            {
                buffer.Clear();
                ais[team].Update(w, buffer);
                foreach (Command c in buffer)
                {
                    if (c is QueueUnitCommand q) { checkedOrders++; if (w.CheckQueue(w.Buildings.First(b => b.Id == q.BuildingId), q.Kind) == QueueCheck.Ok) legal++; }
                    else if (c is BuildCommand bc) { checkedOrders++; if (w.CheckBuild(bc.Kind, bc.X, bc.Y, team) == BuildCheck.Ok) legal++; }
                    w.Enqueue(c, team);
                }
            }
            w.Step();
            Assert.True(w.Player(0).Resources >= 0 && w.Player(1).Resources >= 0, "negative resources at tick " + w.Tick);
        }
        Assert.True(checkedOrders > 20);
        Assert.True(legal * 100 >= checkedOrders * 90, $"only {legal} of {checkedOrders} orders were legal when given");
    }

    [Fact]
    public void TheAi_OnlyTargetsEnemyUnitsItCanSee()
    {
        // The enemy unit sits far from everything the AI owns: it must never be named in an order.
        World w = SkirmishScenario.Create(1);
        Unit hidden = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(58), Fix64.FromInt(30)), 1);
        var ai = new AiPlayer(0, AiDifficulty.Hard, 3);
        var buffer = new List<Command>();
        for (int t = 0; t < 20 * 60; t++)
        {
            buffer.Clear();
            ai.Update(w, buffer);
            foreach (Command c in buffer)
            {
                if (c is AttackCommand a && a.TargetKind == AttackKind.Unit)
                    Assert.NotEqual(hidden.Id, a.TargetId);
                w.Enqueue(c, 0);
            }
            w.Step();
        }
    }

    [Fact]
    public void WithoutAHeadquarters_TheAiDoesNothing()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 50, 50, 0);
        w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(10), Fix64.FromInt(10)), 1);
        var buffer = new List<Command>();
        new AiPlayer(1, AiDifficulty.Hard, 1).Update(w, buffer);
        Assert.Empty(buffer);
    }

    [Fact]
    public void AiThinking_IsCheap()
    {
        World w = SkirmishScenario.Create(3);
        var a = new AiPlayer(0, AiDifficulty.Hard, 1);
        var b = new AiPlayer(1, AiDifficulty.Hard, 2);
        var buffer = new List<Command>();
        for (int t = 0; t < 20 * 120; t++)
        {
            buffer.Clear(); a.Update(w, buffer); foreach (Command c in buffer) w.Enqueue(c, 0);
            buffer.Clear(); b.Update(w, buffer); foreach (Command c in buffer) w.Enqueue(c, 1);
            w.Step();
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int rounds = 200;
        for (int i = 0; i < rounds; i++) { buffer.Clear(); a.Update(w, buffer); buffer.Clear(); b.Update(w, buffer); }
        // Update only works every few ticks, so call it on a tick where it is due: the world tick does not move here.
        Assert.True(sw.Elapsed.TotalMilliseconds / rounds < 2.0);
    }
}
