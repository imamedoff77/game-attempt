using System.Diagnostics;
using System.Net.Sockets;
using SimCore;

namespace GameServer.Tests;

public class ServerTests : IDisposable
{
    private readonly List<MatchServer> _servers = new();
    private readonly List<TestPlayer> _players = new();
    private readonly string _logFolder = Path.Combine(Path.GetTempPath(), "gameserver-tests-" + Guid.NewGuid().ToString("N"));

    public ServerTests()
    {
        // The same step every test host does once at start.
        Defs.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gamedata.json")));
    }

    private MatchServer NewServer(TimeSpan? idle = null, int maxViolations = 20, int maxRooms = 1000, TimeSpan? grace = null, IAccountVerifier? accounts = null)
    {
        var server = new MatchServer(new ServerOptions
        {
            Port = 0, LogFolder = _logFolder, MaxViolationsPerPlayer = maxViolations, MaxRooms = maxRooms,
            IdleTimeout = idle ?? TimeSpan.FromSeconds(10), ReconnectGrace = grace ?? TimeSpan.FromSeconds(60), Accounts = accounts,
        });
        server.Start();
        _servers.Add(server);
        return server;
    }

    private TestPlayer NewPlayer(MatchServer server, int seed = 1)
    {
        var p = new TestPlayer(seed);
        p.Connect(server);
        _players.Add(p);
        return p;
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000, params TestPlayer[] pump)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            foreach (TestPlayer p in pump) p.Pump();
            if (condition()) return true;
            Thread.Sleep(2);
        }
        return false;
    }

    private (TestPlayer host, TestPlayer guest) StartMatch(MatchServer server)
    {
        TestPlayer host = NewPlayer(server, 11), guest = NewPlayer(server, 22);
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host), "no room code");
        guest.Join(host.RoomCode!);
        Assert.True(WaitFor(() => host.Start != null && guest.Start != null, 5000, host, guest), "match did not start");
        return (host, guest);
    }

    /// <summary>Both players play until each has done <paramref name="ticks"/> ticks, as fast as the network lets them.</summary>
    private static bool PlayBoth(TestPlayer a, TestPlayer b, int ticks, int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool busy = a.Pump();
            busy |= b.Pump();
            busy |= a.Play(ticks) > 0;
            busy |= b.Play(ticks) > 0;
            if (a.Client == null || b.Client == null) { Thread.Sleep(1); continue; } // a returning player has no world until the server answers
            if (a.Client.Desynced || b.Client.Desynced) return true;
            if (a.Client.TicksExecuted >= ticks && b.Client.TicksExecuted >= ticks) return true;
            if (!busy) Thread.Sleep(1);
        }
        return false;
    }

    public void Dispose()
    {
        foreach (TestPlayer p in _players) p.Dispose();
        foreach (MatchServer s in _servers) s.StopAsync().GetAwaiter().GetResult();
        try { if (Directory.Exists(_logFolder)) Directory.Delete(_logFolder, true); } catch { }
    }

    // ------------------------------------------------------------------------------------------------ tests

    [Fact]
    public void TwoClients_OverRealSockets_Play5000Ticks_WithIdenticalWorlds()
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);

        Assert.Equal(0, host.Start!.Team);
        Assert.Equal(1, guest.Start!.Team);
        Assert.Equal(host.Start.Seed, guest.Start.Seed);

        Assert.True(PlayBoth(host, guest, 5000), "the match did not finish in time");

        Assert.False(host.Client!.Desynced);
        Assert.False(guest.Client!.Desynced);
        Assert.Equal(0, server.DesyncsDetected);

        // Both stopped at the same tick count, so the worlds must be the same world.
        Assert.Equal(5000, host.Client.TicksExecuted);
        Assert.Equal(5000, guest.Client.TicksExecuted);
        Assert.Equal(host.World!.StateHash(), guest.World!.StateHash());
        Assert.True(host.World.Units.Count(u => u.Id > 8) + guest.World.Units.Count(u => u.Id > 8) > 0, "the bots did nothing");
    }

    [Fact]
    public void BothPlayersGetBothArmoryKits_CleanedUp_AndStayInSync()
    {
        MatchServer server = NewServer();
        TestPlayer host = NewPlayer(server);
        TestPlayer guest = NewPlayer(server);
        var hostKit = new byte[Armory.FromBytes(null).ToBytes().Length];
        hostKit[(int)UnitKind.Tank] = 3;
        host.Kit = hostKit;
        guest.Kit = new byte[] { 255, 200, 99, 4 }; // a cheater, and too short: cut down to the maximum, the rest is zero
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host));
        guest.Join(host.RoomCode!);
        Assert.True(WaitFor(() => host.Start != null && guest.Start != null, 5000, host, guest));

        foreach (TestPlayer p in new[] { host, guest })
        {
            Assert.Equal(3, p.Start!.Kits[0][(int)UnitKind.Tank]);
            Assert.Equal(Armory.MaxLevel, p.Start.Kits[1][0]);
            Assert.Equal(Armory.MaxLevel, p.Start.Kits[1][1]);
            Assert.Equal(Armory.MaxLevel, p.Start.Kits[1][2]);
            Assert.Equal(4, p.Start.Kits[1][3]);
            Assert.Equal(0, p.Start.Kits[1][4]);
        }
        Assert.True(PlayBoth(host, guest, 2000), "the match did not finish in time");
        Assert.False(host.Client!.Desynced);
        Assert.False(guest.Client!.Desynced);
        Assert.Equal(host.World!.StateHash(), guest.World!.StateHash());
    }

    private static byte[] Kit(params (UnitKind kind, int level)[] levels)
    {
        byte[] kit = new Armory().ToBytes();
        foreach ((UnitKind kind, int level) in levels) kit[(int)kind] = (byte)level;
        return kit;
    }

    [Fact]
    public void WithAnAccountCheck_GuestsAndBadTokensAreRefused()
    {
        var accounts = new FakeAccounts();
        MatchServer server = NewServer(accounts: accounts);
        TestPlayer guest = NewPlayer(server);
        guest.Create();
        Assert.True(WaitFor(() => guest.Rejected != null, 5000, guest));
        Assert.Equal(RejectReason.NotSignedIn, guest.Rejected!.Reason);

        TestPlayer forged = NewPlayer(server);
        forged.Auth = "not-a-real-session";
        forged.Create();
        Assert.True(WaitFor(() => forged.Rejected != null, 5000, forged));
        Assert.Equal(RejectReason.NotSignedIn, forged.Rejected!.Reason);
    }

    [Fact]
    public void WithAnAccountCheck_TheArmoryIsLimitedToWhatTheAccountHolds_AndTheSameAccountCannotPlayItself()
    {
        var accounts = new FakeAccounts();
        accounts.Sessions["tok-a"] = new VerifiedPlayer(1, "Anna", Kit((UnitKind.Tank, 2)));
        accounts.Sessions["tok-b"] = new VerifiedPlayer(2, "Boris", Kit());
        MatchServer server = NewServer(accounts: accounts);

        TestPlayer host = NewPlayer(server), guest = NewPlayer(server), twin = NewPlayer(server);
        host.Auth = "tok-a"; host.Kit = Kit((UnitKind.Tank, 5), (UnitKind.Rifleman, 5)); // claims more than the account holds
        guest.Auth = "tok-b"; guest.Kit = Kit((UnitKind.Tank, 5));
        twin.Auth = "tok-a";
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host));

        twin.Join(host.RoomCode!);
        Assert.True(WaitFor(() => twin.Rejected != null, 5000, twin));
        Assert.Equal(RejectReason.SameAccount, twin.Rejected!.Reason);

        guest.Join(host.RoomCode!);
        Assert.True(WaitFor(() => host.Start != null && guest.Start != null, 5000, host, guest));
        Assert.Equal(2, host.Start!.Kits[0][(int)UnitKind.Tank]);
        Assert.Equal(0, host.Start.Kits[0][(int)UnitKind.Rifleman]);
        Assert.Equal(0, host.Start.Kits[1][(int)UnitKind.Tank]);
        Assert.Equal(host.Start.Kits[0], guest.Start!.Kits[0]);
    }

    [Fact]
    public void ASurrender_ReachesBothWorldsInTheSameTurn()
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);
        Assert.True(PlayBoth(host, guest, 100));
        guest.Client!.QueueCommand(new SurrenderCommand());
        Assert.True(PlayBoth(host, guest, 400));
        Assert.True(host.World!.HasSurrendered(1));
        Assert.True(guest.World!.HasSurrendered(1));
        Assert.False(host.World.HasSurrendered(0));
        Assert.False(host.Client!.Desynced);
        Assert.Equal(host.World.StateHash(), guest.World.StateHash());
    }

    [Fact]
    public void ADifferentWorld_IsDetected_BothPlayersAreToldAndTheLogIsSaved()
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);
        guest.Sabotage = w => w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(30), Fix64.FromInt(30)), 1); // only this client sees it

        Assert.True(PlayBoth(host, guest, 3000));
        Assert.True(WaitFor(() => host.Client!.Desynced && guest.Client!.Desynced, 5000, host, guest));

        Assert.True(WaitFor(() => server.DesyncsDetected == 1, 5000)); // counted just after the notices are queued
        Assert.NotNull(host.Client!.DesyncInfo);
        Assert.Equal(host.Client.DesyncInfo.Turn, guest.Client!.DesyncInfo.Turn);
        Assert.NotEqual(host.Client.DesyncInfo.Hash0, host.Client.DesyncInfo.Hash1);

        string[] files = Directory.GetFiles(_logFolder);
        Assert.Contains(files, f => f.EndsWith(".txt"));
        Assert.Contains(files, f => f.EndsWith(".turns") && new FileInfo(f).Length > 0);
    }

    [Fact]
    public void JoiningAnUnknownRoom_IsRefused()
    {
        MatchServer server = NewServer();
        TestPlayer p = NewPlayer(server);
        p.Join("ZZZZ");
        Assert.True(WaitFor(() => p.Rejected != null, 5000, p));
        Assert.Equal(RejectReason.NoSuchRoom, p.Rejected!.Reason);
    }

    [Fact]
    public void ARoomHoldsOnlyTwoPlayers()
    {
        MatchServer server = NewServer();
        var (host, _) = StartMatch(server);
        TestPlayer third = NewPlayer(server, 3);
        third.Join(host.RoomCode!);
        Assert.True(WaitFor(() => third.Rejected != null, 5000, third));
        Assert.Equal(RejectReason.RoomFull, third.Rejected!.Reason);
    }

    [Fact]
    public void PlayersWithDifferentBalanceData_CannotPlayTogether()
    {
        MatchServer server = NewServer();
        TestPlayer host = NewPlayer(server), guest = NewPlayer(server, 2);
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host));
        guest.Join(host.RoomCode!, dataHash: Defs.DataHash ^ 1);
        Assert.True(WaitFor(() => guest.Rejected != null, 5000, guest));
        Assert.Equal(RejectReason.WrongData, guest.Rejected!.Reason);
        Assert.Null(host.Start);
    }

    [Fact]
    public void AnOldClient_IsRefused()
    {
        MatchServer server = NewServer();
        TestPlayer host = NewPlayer(server), guest = NewPlayer(server, 2);
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host));
        guest.Join(host.RoomCode!, version: MessageCodec.ProtocolVersion + 1);
        Assert.True(WaitFor(() => guest.Rejected != null, 5000, guest));
        Assert.Equal(RejectReason.WrongVersion, guest.Rejected!.Reason);
    }

    [Fact]
    public void TheServerRefusesNewRooms_WhenFull()
    {
        MatchServer server = NewServer(maxRooms: 1);
        TestPlayer a = NewPlayer(server), b = NewPlayer(server, 2);
        a.Create();
        Assert.True(WaitFor(() => a.RoomCode != null, 5000, a));
        b.Create();
        Assert.True(WaitFor(() => b.Rejected != null, 5000, b));
        Assert.Equal(RejectReason.ServerBusy, b.Rejected!.Reason);
    }

    [Fact]
    public void GarbageOnTheWire_DropsOnlyThatConnection()
    {
        MatchServer server = NewServer();
        var rng = new Random(5);
        for (int i = 0; i < 30; i++)
        {
            using var raw = new TcpClient("127.0.0.1", server.Port);
            var junk = new byte[rng.Next(1, 3000)];
            rng.NextBytes(junk);
            if (i % 3 == 0) { junk[0] = junk[1] = 0x7f; }   // an absurd length
            if (i % 3 == 1) { junk[0] = junk[1] = junk[2] = 0; junk[3] = 3; } // a plausible length, nonsense contents
            raw.GetStream().Write(junk, 0, junk.Length);
        }

        // A normal match still works afterwards.
        var (host, guest) = StartMatch(server);
        Assert.True(PlayBoth(host, guest, 400));
        Assert.Equal(host.World!.StateHash(), guest.World!.StateHash());
    }

    [Fact]
    public void ASilentPlayer_IsDroppedAfterTheTimeout_AndTheOpponentIsTold()
    {
        MatchServer server = NewServer(idle: TimeSpan.FromMilliseconds(800));
        var (host, guest) = StartMatch(server);

        // The host keeps the link alive by playing and pinging; the guest says nothing at all and is never pumped.
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 4000 && host.PeerLeft == null)
        {
            host.Pump();
            host.Link.Send(new Ping());
            Thread.Sleep(100);
        }
        Assert.NotNull(host.PeerLeft);
        Assert.Equal(1, host.PeerLeft!.Team);
    }

    [Fact]
    public void ACheatingClient_SendingOutOfOrderTurns_IsKicked()
    {
        MatchServer server = NewServer(maxViolations: 5);
        var (host, guest) = StartMatch(server);

        for (int i = 0; i < 20; i++) guest.Link.Send(new TurnSubmit(500 + i, new List<Command>())); // turns it has no right to send yet

        Assert.True(WaitFor(() => guest.Link.Closed || host.PeerLeft != null, 5000, host, guest));
        Assert.True(WaitFor(() => host.PeerLeft != null, 5000, host));
        Assert.Equal(1, host.PeerLeft!.Team);
    }

    [Fact]
    public void AClientThatSendsServerMessages_IsRefused()
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);
        guest.Link.Send(new MatchStart(0, 2, 1)); // pretending to be the server
        Assert.True(WaitFor(() => host.PeerLeft != null, 5000, host, guest));
        Assert.Equal(1, host.PeerLeft!.Team);
    }

    [Fact]
    public void ARoomIsRemoved_WhenEveryoneLeaves()
    {
        MatchServer server = NewServer();
        TestPlayer host = NewPlayer(server);
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host));
        Assert.Equal(1, server.RoomCount);
        host.Dispose();
        Assert.True(WaitFor(() => server.RoomCount == 0, 5000));
    }

    [Fact]
    public void ManyMatchesAtOnce_AllStayInSync()
    {
        MatchServer server = NewServer();
        var matches = new List<(TestPlayer, TestPlayer)>();
        for (int i = 0; i < 8; i++) matches.Add(StartMatch(server));

        Parallel.ForEach(matches, m => Assert.True(PlayBoth(m.Item1, m.Item2, 1500), "a match did not finish"));

        foreach (var (a, b) in matches)
        {
            Assert.False(a.Client!.Desynced);
            Assert.Equal(a.World!.StateHash(), b.World!.StateHash());
        }
        Assert.Equal(0, server.DesyncsDetected);
    }

    // ---- reconnecting --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)] // the old connection is gone for good
    [InlineData(true)]  // the old connection still looks alive to the server (phone left the network without saying goodbye)
    public void APlayerWhoLosesTheLink_ComesBack_CatchesUp_AndTheMatchGoesOn(bool oldLinkStillOpen)
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);
        Assert.True(PlayBoth(host, guest, 1500));

        GameConnection old = guest.Reconnect(server, keepOldOpen: oldLinkStillOpen);
        if (!oldLinkStillOpen) Assert.True(WaitFor(() => host.PeerLeft != null, 5000, host));

        // Back in: the guest's world is rebuilt from the server's record of every turn, then both play on together.
        Assert.True(PlayBoth(host, guest, 4000), "the match did not continue");
        old.Dispose();

        Assert.Equal(1, guest.Resumed);
        Assert.False(host.Client!.Desynced);
        Assert.False(guest.Client!.Desynced);
        Assert.Equal(0, server.DesyncsDetected);
        Assert.Equal(1, server.Rejoins);
        Assert.Equal(host.World!.StateHash(), guest.World!.StateHash());
        if (!oldLinkStillOpen) Assert.True(WaitFor(() => host.PeerCameBack, 5000, host));
    }

    [Fact]
    public void ReconnectingTwice_AndBothPlayersReconnecting_StillEndsInTheSameWorld()
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);
        Assert.True(PlayBoth(host, guest, 800));
        guest.Reconnect(server);
        Assert.True(PlayBoth(host, guest, 1600));
        host.Reconnect(server);
        guest.Reconnect(server);
        Assert.True(PlayBoth(host, guest, 2600));

        Assert.False(host.Client!.Desynced || guest.Client!.Desynced);
        Assert.Equal(0, server.DesyncsDetected);
        Assert.Equal(host.World!.StateHash(), guest.World!.StateHash());
    }

    [Fact]
    public void SomeoneElse_CannotTakeAPlayersSeat()
    {
        MatchServer server = NewServer();
        var (host, guest) = StartMatch(server);
        TestPlayer thief = NewPlayer(server, 9);
        thief.Code = host.RoomCode;
        thief.Start = guest.Start; // knows the room and the team, but not the secret
        thief.Reconnect(server, keepOldOpen: true, wrongToken: guest.Start!.Token ^ 1);
        Assert.True(WaitFor(() => thief.Rejected != null, 5000, thief));
        Assert.Equal(RejectReason.BadRequest, thief.Rejected!.Reason);

        // The real guest is untouched and the match goes on.
        Assert.True(PlayBoth(host, guest, 600));
        Assert.Equal(host.World!.StateHash(), guest.World!.StateHash());
    }

    [Fact]
    public void IfNobodyComesBack_TheMatchIsGivenUp_AndLateReturnersAreRefused()
    {
        MatchServer server = NewServer(grace: TimeSpan.FromMilliseconds(700));
        var (host, guest) = StartMatch(server);
        Assert.True(PlayBoth(host, guest, 300));
        guest.Link.Dispose();

        Assert.True(WaitFor(() => host.PeerLeft != null, 5000, host));
        Assert.True(WaitFor(() => host.Rejected != null, 6000, host), "the remaining player was not told");
        Assert.Equal(RejectReason.OpponentGone, host.Rejected!.Reason);
        Assert.True(WaitFor(() => host.Link.Closed, 6000, host), "the remaining player was not released");
        Assert.True(WaitFor(() => server.RoomCount == 0, 6000));

        TestPlayer late = NewPlayer(server, 7);
        late.Code = guest.Code; late.Start = guest.Start;
        late.Reconnect(server);
        Assert.True(WaitFor(() => late.Rejected != null, 5000, late));
        Assert.Equal(RejectReason.NoSuchRoom, late.Rejected!.Reason);
    }
}
