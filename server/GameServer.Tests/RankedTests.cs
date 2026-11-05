using System.Diagnostics;
using SimCore;

namespace GameServer.Tests;

public class RankedTests : IDisposable
{
    private readonly List<MatchServer> _servers = new();
    private readonly List<TestPlayer> _players = new();

    public RankedTests()
    {
        Defs.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gamedata.json")));
    }

    public void Dispose()
    {
        foreach (TestPlayer p in _players) p.Dispose();
        foreach (MatchServer s in _servers) s.StopAsync().GetAwaiter().GetResult();
    }

    private MatchServer NewServer(IAccountVerifier? accounts, TimeSpan? grace = null, int windowBase = 100, int windowPerSecond = 50, TimeSpan? reportWait = null, bool verify = false)
    {
        var server = new MatchServer(new ServerOptions
        {
            Port = 0, Accounts = accounts, ReconnectGrace = grace ?? TimeSpan.FromSeconds(60),
            VerifyResults = verify, QueueWindowBase = windowBase, QueueWindowPerSecond = windowPerSecond, ReportWait = reportWait ?? TimeSpan.FromSeconds(30),
        });
        server.Start();
        _servers.Add(server);
        return server;
    }

    private TestPlayer Player(MatchServer server, string token = "")
    {
        var p = new TestPlayer(1) { Auth = token };
        p.Connect(server);
        _players.Add(p);
        return p;
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs, params TestPlayer[] pump)
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

    private static FakeAccounts Accounts(params (string token, long id, int rating)[] players)
    {
        var accounts = new FakeAccounts();
        foreach ((string token, long id, int rating) in players)
        {
            accounts.Sessions[token] = new VerifiedPlayer(id, "P" + id, new Armory().ToBytes());
            accounts.Ratings[id] = rating;
        }
        return accounts;
    }

    [Fact]
    public void TwoPlayersWithCloseRatings_AreMatched_GetEachOthersRatings_AndOneSitsOnEachSide()
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("b", 2, 1060));
        MatchServer server = NewServer(accounts);
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Quick();
        Assert.True(WaitFor(() => a.InLine != null, 5000, a));
        Assert.Equal(1000, a.InLine!.Rating);
        Assert.Equal(1, server.QueueLength);

        b.Quick();
        Assert.True(WaitFor(() => a.Start != null && b.Start != null && a.Info != null && b.Info != null, 5000, a, b), "no match");
        Assert.NotEqual(a.Start!.Team, b.Start!.Team);
        Assert.Equal(a.Start.Seed, b.Start.Seed);
        int[] ratingsByTeam = new int[2];
        ratingsByTeam[a.Start.Team] = 1000;
        ratingsByTeam[b.Start.Team] = 1060;
        Assert.Equal(ratingsByTeam, a.Info!.Ratings);
        Assert.Equal(ratingsByTeam, b.Info!.Ratings);
        Assert.Equal(0, server.QueueLength);
    }

    [Fact]
    public void PlayersFarApart_WaitUntilTheWindowGrows()
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("b", 2, 1500));
        MatchServer server = NewServer(accounts, windowBase: 100, windowPerSecond: 250); // 500 apart: ok after about 2 s
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Quick();
        b.Quick();
        Assert.True(WaitFor(() => b.InLine != null, 5000, a, b));
        Thread.Sleep(300);
        a.Pump(); b.Pump();
        Assert.Null(a.Start);
        Assert.True(WaitFor(() => a.Start != null && b.Start != null, 8000, a, b), "the window never grew");
    }

    [Fact]
    public void TheClosestOpponentIsChosen_AndTheSameAccountIsNeverPairedWithItself()
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("far", 2, 1090), ("near", 3, 1010));
        accounts.Sessions["a-again"] = accounts.Sessions["a"];
        MatchServer server = NewServer(accounts);
        TestPlayer far = Player(server, "far"), a = Player(server, "a"), twin = Player(server, "a-again"), near = Player(server, "near");
        far.Quick();
        Assert.True(WaitFor(() => far.InLine != null, 5000, far));
        near.Quick();
        Assert.True(WaitFor(() => near.InLine != null || near.Start != null, 5000, near));
        // far and near are within the window of each other and get paired right away; the one queued first is far
        Assert.True(WaitFor(() => far.Start != null && near.Start != null, 5000, far, near));

        a.Quick();
        Assert.True(WaitFor(() => a.InLine != null, 5000, a));
        twin.Quick();
        Assert.True(WaitFor(() => twin.Rejected != null, 5000, twin));
        Assert.Equal(RejectReason.SameAccount, twin.Rejected!.Reason);
        Assert.Null(a.Start);
    }

    [Fact]
    public void LeavingTheLine_RemovesThePlayer_AndGuestsOnAnOpenServerAreMatchedWithoutRating()
    {
        MatchServer server = NewServer(accounts: null);
        TestPlayer g1 = Player(server), g2 = Player(server), g3 = Player(server);
        g1.Quick();
        Assert.True(WaitFor(() => g1.InLine != null, 5000, g1));
        g1.Link.Send(new LeaveQueue());
        Assert.True(WaitFor(() => server.QueueLength == 0, 5000, g1));

        g2.Quick(); g3.Quick();
        Assert.True(WaitFor(() => g2.Start != null && g3.Start != null, 5000, g2, g3));
        Thread.Sleep(200);
        g2.Pump();
        Assert.Null(g2.Info); // unranked: no rating information
    }

    [Fact]
    public void ABrokenConnectionInTheLine_DoesNotLeaveAGhost()
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("b", 2, 1000));
        MatchServer server = NewServer(accounts);
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Quick();
        Assert.True(WaitFor(() => a.InLine != null, 5000, a));
        a.Link.Dispose();
        Assert.True(WaitFor(() => server.QueueLength == 0, 5000));
        b.Quick();
        Assert.True(WaitFor(() => b.InLine != null, 5000, b));
        Thread.Sleep(300);
        b.Pump();
        Assert.Null(b.Start);
    }

    private (TestPlayer a, TestPlayer b, FakeAccounts accounts, MatchServer server) RankedMatch(TimeSpan? grace = null, TimeSpan? reportWait = null)
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("b", 2, 1000));
        MatchServer server = NewServer(accounts, grace, reportWait: reportWait);
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Quick(); b.Quick();
        Assert.True(WaitFor(() => a.Start != null && b.Start != null, 5000, a, b));
        return (a, b, accounts, server);
    }

    [Fact]
    public void WhenBothSayWhoWon_TheMatchIsCountedOnce_AndBothHearTheirNewRating()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, MatchServer server) = RankedMatch();
        int winnerTeam = a.Start!.Team;
        a.ReportWinner(winnerTeam);
        Thread.Sleep(100);
        Assert.Empty(accounts.Matches); // one voice is not enough while the other player is still here
        b.ReportWinner(winnerTeam);
        Assert.True(WaitFor(() => a.Rated != null && b.Rated != null, 5000, a, b));
        Assert.Equal((1000, 1016), (a.Rated!.OldRating, a.Rated.NewRating));
        Assert.Equal((1000, 984), (b.Rated!.OldRating, b.Rated.NewRating));
        Assert.Equal(1016, accounts.Ratings[1]);
        Assert.Equal(984, accounts.Ratings[2]);
        a.ReportWinner(winnerTeam); // a repeat changes nothing
        Thread.Sleep(100);
        Assert.Single(accounts.Matches);
        Assert.Equal(1, server.RatedMatchesCounted);
    }

    [Fact]
    public void WhenThePlayersDisagree_NothingIsCounted()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = RankedMatch();
        a.ReportWinner(a.Start!.Team);
        b.ReportWinner(b.Start!.Team); // both claim to have won
        Thread.Sleep(400);
        a.Pump(); b.Pump();
        Assert.Empty(accounts.Matches);
        Assert.Null(a.Rated);
    }

    [Fact]
    public void WhenOnePlayerReportsAndTheOtherLeaves_TheReporterIsBelieved()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = RankedMatch();
        a.ReportWinner(a.Start!.Team);
        b.Link.Dispose();
        Assert.True(WaitFor(() => a.Rated != null, 5000, a), "the result was not counted");
        Assert.Equal(1016, accounts.Ratings[1]);
        Assert.Equal(984, accounts.Ratings[2]);
    }

    [Fact]
    public void AReportThatTheOtherNeverAnswers_IsBelievedAfterTheWait()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = RankedMatch(reportWait: TimeSpan.FromMilliseconds(600));
        a.ReportWinner(a.Start!.Team);
        Assert.True(WaitFor(() => a.Rated != null && b.Rated != null, 6000, a, b));
        Assert.Equal(1016, accounts.Ratings[1]);
    }

    [Fact]
    public void LeavingAMatchForGood_IsALoss_ByWalkover()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = RankedMatch(grace: TimeSpan.FromMilliseconds(800));
        b.Link.Dispose(); // b rage-quits without reporting anything
        Assert.True(WaitFor(() => a.Rated != null, 8000, a), "no walkover result");
        Assert.Equal(1016, accounts.Ratings[1]);
        Assert.Equal(984, accounts.Ratings[2]);
        Assert.Equal(RejectReason.OpponentGone, a.Rejected?.Reason);
    }

    [Fact]
    public void FriendRooms_AreNeverRated()
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("b", 2, 1000));
        MatchServer server = NewServer(accounts);
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Create();
        Assert.True(WaitFor(() => a.RoomCode != null, 5000, a));
        b.Join(a.RoomCode!);
        Assert.True(WaitFor(() => a.Start != null && b.Start != null, 5000, a, b));
        Assert.Null(a.Info);
        a.ReportWinner(0);
        b.ReportWinner(0);
        Thread.Sleep(400);
        Assert.Empty(accounts.Matches);
    }

    [Fact]
    public void IfTheAccountServerIsDown_TheMatchEndsWithoutARatingChange_AndNothingCrashes()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = RankedMatch();
        accounts.Down = true;
        a.ReportWinner(a.Start!.Team);
        b.ReportWinner(a.Start.Team);
        Thread.Sleep(500);
        a.Pump(); b.Pump();
        Assert.Null(a.Rated);
        Assert.Empty(accounts.Matches);
    }

    // ---- results checked by replaying the match ----

    private (TestPlayer a, TestPlayer b, FakeAccounts accounts, MatchServer server) VerifiedMatch(TimeSpan? grace = null)
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000), ("b", 2, 1000));
        MatchServer server = NewServer(accounts, grace, verify: true);
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Quick(); b.Quick();
        Assert.True(WaitFor(() => a.Start != null && b.Start != null, 5000, a, b));
        return (a, b, accounts, server);
    }

    private static void PlayBoth(TestPlayer a, TestPlayer b, int ticks)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 30000)
        {
            bool busy = a.Pump();
            busy |= b.Pump();
            busy |= a.Play(ticks) > 0;
            busy |= b.Play(ticks) > 0;
            if (a.Client != null && b.Client != null && a.Client.TicksExecuted >= ticks && b.Client.TicksExecuted >= ticks) return;
            if (!busy) Thread.Sleep(1);
        }
        Assert.Fail("the match did not get far enough");
    }

    [Fact]
    public void Replay_DecidesTheWinner_EvenWhenBothPlayersLie()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = VerifiedMatch();
        a.Client!.QueueCommand(new SurrenderCommand()); // a gives up, so b really won
        PlayBoth(a, b, 120);
        a.ReportWinner(a.Start!.Team); // both claim to have won
        b.ReportWinner(b.Start!.Team);
        Assert.True(WaitFor(() => a.Rated != null && b.Rated != null, 10000, a, b), "the result was not counted");
        Assert.Equal(984, a.Rated!.NewRating); // a lost
        Assert.Equal(1016, b.Rated!.NewRating);
        Assert.Single(accounts.Matches);
    }

    [Fact]
    public void AFalseClaimInTheMiddleOfAMatch_ChangesNothing()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = VerifiedMatch();
        PlayBoth(a, b, 80);
        a.ReportWinner(a.Start!.Team);
        Thread.Sleep(800);
        a.Pump(); b.Pump();
        Assert.Empty(accounts.Matches);
        Assert.Null(a.Rated);

        // and the real end still counts afterwards
        b.Client!.QueueCommand(new SurrenderCommand());
        PlayBoth(a, b, 160);
        b.ReportWinner(a.Start.Team);
        Assert.True(WaitFor(() => a.Rated != null, 10000, a, b));
        Assert.Equal(1016, accounts.Ratings[1]);
    }

    [Fact]
    public void ALoneReport_IsCountedAtOnce_WhenTheReplayBacksItUp()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = VerifiedMatch();
        a.Client!.QueueCommand(new SurrenderCommand());
        PlayBoth(a, b, 120);
        b.ReportWinner(b.Start!.Team); // a never answers (not even disconnected)
        Assert.True(WaitFor(() => b.Rated != null, 10000, a, b));
        Assert.Equal(1016, b.Rated!.NewRating);
    }

    [Fact]
    public void AWalkoverGoesToTheRealWinner_NotToWhoeverStayed()
    {
        (TestPlayer a, TestPlayer b, FakeAccounts accounts, _) = VerifiedMatch(grace: TimeSpan.FromMilliseconds(800));
        a.Client!.QueueCommand(new SurrenderCommand());
        PlayBoth(a, b, 120);
        b.Link.Dispose(); // the winner leaves without a word; the loser sits on the end screen
        Assert.True(WaitFor(() => a.Rated != null, 10000, a), "no result");
        Assert.Equal(984, a.Rated!.NewRating); // the one who stayed still lost
        Assert.Equal(1016, accounts.Ratings[2]);
    }

    [Fact]
    public void QuickMatch_RefusesPlayersWhoseBalanceDataDiffers_WhenResultsAreVerified()
    {
        FakeAccounts accounts = Accounts(("a", 1, 1000));
        MatchServer server = NewServer(accounts, verify: true);
        TestPlayer a = Player(server, "a");
        a.Quick(dataHash: 12345);
        Assert.True(WaitFor(() => a.Rejected != null, 5000, a));
        Assert.Equal(RejectReason.WrongData, a.Rejected!.Reason);
    }

    // ---- ready-made chat phrases ----

    [Fact]
    public void APhrase_ReachesOnlyTheOpponent_WithTheSendersTeam_AndFastRepeatsAreDropped()
    {
        (TestPlayer a, TestPlayer b, _, _) = RankedMatch();
        a.Say(3);
        a.Say(4); // too soon after the first
        Assert.True(WaitFor(() => b.Heard.Count > 0, 5000, a, b));
        Thread.Sleep(300);
        a.Pump(); b.Pump();
        Assert.Single(b.Heard);
        Assert.Equal((a.Start!.Team, 3), (b.Heard[0].Team, b.Heard[0].Preset));
        Assert.Empty(a.Heard); // nobody hears their own phrase back

        Thread.Sleep(1100);
        a.Say(5);
        Assert.True(WaitFor(() => b.Heard.Count == 2, 5000, a, b));
        Assert.Equal(5, b.Heard[1].Preset);
    }

    [Fact]
    public void APhraseOutsideAMatch_IsIgnored()
    {
        MatchServer server = NewServer(accounts: null);
        TestPlayer host = Player(server), other = Player(server);
        host.Create();
        Assert.True(WaitFor(() => host.RoomCode != null, 5000, host));
        host.Say(1); // alone in the room
        other.Join(host.RoomCode!);
        Assert.True(WaitFor(() => host.Start != null && other.Start != null, 5000, host, other));
        Thread.Sleep(300);
        other.Pump();
        Assert.Empty(other.Heard);
    }

    [Fact]
    public void QuickMatches_AreFoughtWithoutArmoryBonuses()
    {
        FakeAccounts accounts = new();
        byte[] strong = new Armory().ToBytes();
        Array.Fill(strong, (byte)Armory.MaxLevel);
        accounts.Sessions["a"] = new VerifiedPlayer(1, "A", strong);
        accounts.Sessions["b"] = new VerifiedPlayer(2, "B", strong);
        MatchServer server = NewServer(accounts);
        TestPlayer a = Player(server, "a"), b = Player(server, "b");
        a.Kit = strong; b.Kit = strong; // both bring the best Armory there is
        a.Quick(); b.Quick();
        Assert.True(WaitFor(() => a.Start != null && b.Start != null, 5000, a, b));
        Assert.All(a.Start!.Kits, kit => Assert.All(kit, level => Assert.Equal(0, level)));
        Assert.Equal(a.Start.Kits[0], b.Start!.Kits[0]);
    }
}
