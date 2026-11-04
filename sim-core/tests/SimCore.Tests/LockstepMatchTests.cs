using SimCore;

namespace SimCore.Tests;

/// <summary>
/// Two complete clients and a relay joined by a simulated bad network (delays, uneven frame rates, stalls).
/// Computer players give random orders. Both worlds must end up identical, whatever the network did.
/// </summary>
public class LockstepMatchTests
{
    private const int StepMs = 5;       // resolution of the virtual clock
    private const int TickMs = 50;      // 20 ticks per second

    private static World NewWorld() => OnlineScenario.Create();

    private sealed class Link
    {
        private readonly Queue<(long due, byte[] data)> _queue = new();
        private long _lastDue;
        public bool Blocked;

        public void Send(NetMessage message, long now, int latencyMs)
        {
            byte[] bytes = MessageCodec.Encode(message); // everything goes through the real byte format
            _lastDue = Math.Max(_lastDue, now + latencyMs); // a TCP-like link never reorders
            _queue.Enqueue((_lastDue, bytes));
        }

        public IEnumerable<NetMessage> Arrived(long now)
        {
            while (!Blocked && _queue.Count > 0 && _queue.Peek().due <= now)
            {
                byte[] data = _queue.Dequeue().data;
                Assert.True(MessageCodec.TryDecode(data, out NetMessage m));
                yield return m;
            }
        }
    }

    private sealed class Match
    {
        public readonly World[] Worlds = { NewWorld(), NewWorld() };
        public readonly LockstepClient[] Clients = new LockstepClient[2];
        public readonly LockstepRelay Relay;
        public readonly Link[] ToServer = { new Link(), new Link() };
        public readonly Link[] ToClient = { new Link(), new Link() };
        public long Now;

        private readonly Random _rng;
        private readonly int _maxLatency, _spikeLatency;
        private readonly int[] _framePeriod = new int[2];
        private readonly long[] _nextFrame = new long[2];
        private readonly long[] _lastFrame = new long[2];
        private readonly long[] _nextThink = new long[2];
        private readonly int[] _acc = new int[2];
        private readonly Random[] _bots;
        public int TargetTicks;
        public bool BotsEnabled = true;
        public Action<Match, int> BeforeFrame; // test hook: called each frame with the team number

        public Match(int seed, int inputDelay, int maxLatencyMs, int spikeLatencyMs, int period0, int period1)
        {
            _rng = new Random(seed);
            _maxLatency = maxLatencyMs; _spikeLatency = spikeLatencyMs;
            _framePeriod[0] = period0; _framePeriod[1] = period1;
            _bots = new[] { new Random(seed * 31 + 1), new Random(seed * 31 + 2) };
            Relay = new LockstepRelay((to, msg) => ToClient[to].Send(msg, Now, Latency()));
            for (int t = 0; t < 2; t++)
            {
                int team = t;
                Clients[t] = new LockstepClient(Worlds[t], team, inputDelay, msg => ToServer[team].Send(msg, Now, Latency()));
            }
            for (int t = 0; t < 2; t++) Clients[t].Start();
        }

        private int Latency()
        {
            int latency = _rng.Next(0, _maxLatency + 1);
            if (_spikeLatency > 0 && _rng.Next(50) == 0) latency += _rng.Next(0, _spikeLatency); // now and then a long hiccup
            return latency;
        }

        /// <summary>Runs until both clients have played TargetTicks ticks (or a time limit passes). Returns false on timeout.</summary>
        public bool RunToTarget(long timeLimitMs)
        {
            while (Now < timeLimitMs)
            {
                if (Clients.All(c => c.TicksExecuted >= TargetTicks || c.Desynced)) return true;
                Advance();
            }
            return false;
        }

        public void Advance()
        {
            Now += StepMs;
            for (int t = 0; t < 2; t++)
                foreach (NetMessage m in ToServer[t].Arrived(Now)) Relay.Receive(t, m);
            for (int t = 0; t < 2; t++)
                foreach (NetMessage m in ToClient[t].Arrived(Now)) Clients[t].Receive(m);

            for (int t = 0; t < 2; t++)
            {
                if (Now < _nextFrame[t]) continue;
                int dt = (int)(Now - _lastFrame[t]);
                _lastFrame[t] = Now;
                _nextFrame[t] = Now + _framePeriod[t];

                BeforeFrame?.Invoke(this, t);

                if (BotsEnabled && Now >= _nextThink[t])
                {
                    _nextThink[t] = Now + 300 + _bots[t].Next(0, 400);
                    Command c = MatchBot.Next(Worlds[t], t, _bots[t]);
                    if (c != null) Clients[t].QueueCommand(c);
                }

                // Like a real game loop: collect elapsed time, play as many ticks as time allows, never more than a handful per frame.
                _acc[t] = Math.Min(_acc[t] + dt, TickMs * 8);
                while (_acc[t] >= TickMs && Clients[t].TicksExecuted < TargetTicks && Clients[t].TryStep())
                    _acc[t] -= TickMs;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ tests

    [Theory]
    [InlineData(1, 2, 30, 0, 15, 15)]
    [InlineData(2, 2, 150, 0, 15, 35)]
    [InlineData(3, 3, 300, 400, 35, 15)]
    [InlineData(4, 2, 0, 0, 5, 5)]
    [InlineData(5, 4, 600, 1500, 20, 40)]
    public void TwoClients_OverABadNetwork_EndWithIdenticalWorlds(int seed, int delay, int latency, int spike, int period0, int period1)
    {
        var m = new Match(seed, delay, latency, spike, period0, period1) { TargetTicks = 1200 };
        Assert.True(m.RunToTarget(10 * 60 * 1000), "the match did not finish in time");

        Assert.False(m.Relay.Desynced);
        Assert.Equal(0, m.Relay.Violations);
        Assert.True(m.Relay.HashesCompared >= 1200 / (LockstepClient.TicksPerTurn * LockstepClient.HashIntervalTurns) - 2);
        Assert.Equal(1200, m.Clients[0].TicksExecuted);
        Assert.Equal(1200, m.Clients[1].TicksExecuted);
        Assert.Equal(m.Worlds[0].StateHash(), m.Worlds[1].StateHash());
    }

    [Fact]
    public void ManyRandomMatches_NeverDesync()
    {
        int busy = 0;
        for (int seed = 100; seed < 160; seed++)
        {
            var rng = new Random(seed);
            var m = new Match(seed, rng.Next(2, 5), rng.Next(0, 400), rng.Next(0, 3) == 0 ? rng.Next(100, 1200) : 0, 5 * rng.Next(1, 9), 5 * rng.Next(1, 9)) { TargetTicks = 900 };
            Assert.True(m.RunToTarget(15 * 60 * 1000), "seed " + seed + " timed out");
            Assert.False(m.Relay.Desynced, "seed " + seed);
            Assert.Equal(0, m.Relay.Violations);
            Assert.Equal(m.Worlds[0].StateHash(), m.Worlds[1].StateHash());
            if (m.Worlds[0].Units.Any(u => u.Id > 8)) busy++; // units were built during the match
        }
        Assert.True(busy >= 30, "only " + busy + " of 60 random games produced any units: the test must exercise production");
    }

    [Fact]
    public void ALongMatch_FiveMinutes_StaysInSync_AndSomethingGotDestroyed()
    {
        var m = new Match(2026, 2, 120, 600, 15, 25) { TargetTicks = 6000 };
        Assert.True(m.RunToTarget(30 * 60 * 1000));
        Assert.False(m.Relay.Desynced);
        Assert.Equal(m.Worlds[0].StateHash(), m.Worlds[1].StateHash());
        Assert.Equal(6000, m.Worlds[0].Tick);
        int startUnits = 8;
        bool fought = m.Worlds[0].Units.Any(u => u.Hp < u.MaxHp) || m.Worlds[0].Buildings.Any(b => b.Hp < b.MaxHp) || m.Worlds[0].Units.Count != startUnits;
        Assert.True(fought);
    }

    [Fact]
    public void ReplayingTheServerLog_InAFreshWorld_GivesTheSameWorld()
    {
        // This is what a returning player will do: take the whole log, play it from the start, arrive at the same world.
        var m = new Match(5, 2, 80, 0, 15, 15) { TargetTicks = 1200 };
        Assert.True(m.RunToTarget(10 * 60 * 1000));

        World fresh = NewWorld();
        for (int turn = 0; turn < 1200 / LockstepClient.TicksPerTurn; turn++)
        {
            TurnBundle b = m.Relay.Log[turn];
            for (int team = 0; team < 2; team++)
                foreach (Command c in b.ByTeam[team]) fresh.Enqueue(c, team);
            for (int i = 0; i < LockstepClient.TicksPerTurn; i++) fresh.Step();
        }
        Assert.Equal(m.Worlds[0].StateHash(), fresh.StateHash());
    }

    [Fact]
    public void ADesyncedWorld_IsDetected_WithinAboutASecond_AndBothClientsStop()
    {
        var m = new Match(9, 2, 40, 0, 15, 15) { TargetTicks = 2000 };
        bool sabotaged = false;
        m.BeforeFrame = (match, team) =>
        {
            if (team == 1 && !sabotaged && match.Clients[1].TicksExecuted >= 200)
            {
                sabotaged = true;
                match.Worlds[1].SpawnUnit(UnitKind.Jeep, new Vec2(Fix64.FromInt(30), Fix64.FromInt(30)), 1); // something only one side has
            }
        };
        m.RunToTarget(10 * 60 * 1000);

        Assert.True(sabotaged);
        Assert.True(m.Relay.Desynced);
        Assert.True(m.Clients[0].Desynced && m.Clients[1].Desynced);
        // found at the first fingerprint after the damage: sabotage at tick ~200, fingerprints every 20 ticks
        Assert.InRange(m.Relay.DesyncInfo.Turn * LockstepClient.TicksPerTurn, 200, 200 + 2 * LockstepClient.TicksPerTurn * LockstepClient.HashIntervalTurns + 60);
        Assert.NotEqual(m.Relay.DesyncInfo.Hash0, m.Relay.DesyncInfo.Hash1);
        // and they really stopped
        int t0 = m.Clients[0].TicksExecuted;
        for (int i = 0; i < 400; i++) m.Advance();
        Assert.Equal(t0, m.Clients[0].TicksExecuted);
    }

    [Fact]
    public void WhenTheNetworkStops_TheGameWaits_ThenCatchesUp_WithoutDesync()
    {
        var m = new Match(7, 2, 30, 0, 15, 15) { TargetTicks = 1500 };
        // run a while, then cut client 1 off completely
        while (m.Clients[0].TicksExecuted < 300) m.Advance();
        m.ToClient[1].Blocked = true;
        m.ToServer[1].Blocked = true;
        for (int i = 0; i < 2000; i++) m.Advance(); // 10 s of silence

        Assert.True(m.Clients[0].Waiting || m.Clients[1].Waiting);
        int stuck0 = m.Clients[0].TicksExecuted, stuck1 = m.Clients[1].TicksExecuted;
        // nobody may run away: at most the input delay's worth of turns beyond the last complete turn
        Assert.True(stuck0 <= 340 + (2 + 2) * LockstepClient.TicksPerTurn + 80, "client 0 ran on: " + stuck0);
        for (int i = 0; i < 200; i++) m.Advance();
        Assert.Equal(stuck0, m.Clients[0].TicksExecuted);
        Assert.Equal(stuck1, m.Clients[1].TicksExecuted);

        m.ToClient[1].Blocked = false;
        m.ToServer[1].Blocked = false;
        Assert.True(m.RunToTarget(m.Now + 15 * 60 * 1000));
        Assert.False(m.Relay.Desynced);
        Assert.Equal(m.Worlds[0].StateHash(), m.Worlds[1].StateHash());
    }

    [Fact]
    public void OrdersForTheOpponentsUnits_ThroughTheWholeStack_DoNothing()
    {
        var m = new Match(11, 2, 20, 0, 15, 15) { TargetTicks = 400, BotsEnabled = false };
        Unit enemyUnit = m.Worlds[0].Units.First(u => u.Team == 1);
        Vec2 before = enemyUnit.Pos;
        bool sent = false;
        m.BeforeFrame = (match, team) =>
        {
            if (team == 0 && !sent)
            {
                sent = true; // client 0 tries to steer client 1's soldier, and to spend client 1's money
                match.Clients[0].QueueCommand(new MoveCommand(new[] { enemyUnit.Id }, new Vec2(Fix64.FromInt(2), Fix64.FromInt(2))));
                Building theirBarracks = match.Worlds[0].Buildings.First(b => b.Team == 1 && b.Kind == BuildingKind.Barracks);
                match.Clients[0].QueueCommand(new QueueUnitCommand(theirBarracks.Id, UnitKind.Tank));
            }
        };
        Assert.True(m.RunToTarget(5 * 60 * 1000));
        foreach (World w in m.Worlds)
        {
            Unit u = w.GetUnit(enemyUnit.Id);
            Assert.False(u.Moving);
            Assert.Equal(before.X.Raw, u.Pos.X.Raw);
            Assert.True(w.Player(1).Resources >= Economy.StartResources, "nothing may have been bought with their money");
            Assert.Empty(w.Buildings.First(b => b.Team == 1 && b.Kind == BuildingKind.Barracks).Queue);
        }
        Assert.Equal(m.Worlds[0].StateHash(), m.Worlds[1].StateHash());
    }

    [Fact]
    public void ManyCommandsInOneBurst_AreCappedAtWhatATurnCanCarry()
    {
        var m = new Match(13, 2, 10, 0, 15, 15) { TargetTicks = 200, BotsEnabled = false };
        for (int i = 0; i < 500; i++) m.Clients[0].QueueCommand(new CancelQueuedCommand(1, 0));
        Assert.True(m.RunToTarget(5 * 60 * 1000));
        Assert.Equal(0, m.Relay.Violations);
        Assert.Equal(m.Worlds[0].StateHash(), m.Worlds[1].StateHash());
    }
}
