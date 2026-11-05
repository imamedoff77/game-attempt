using SimCore;
using SimCore.Tests;

namespace GameServer.Tests;

/// <summary>A real client for tests: a TCP connection plus its own world and lockstep engine, driven by hand from the test thread.</summary>
internal sealed class TestPlayer : IDisposable
{
    public GameConnection Link = new();
    public string? Code;
    public bool PeerCameBack;
    public int Resumed;
    public World? World;
    public LockstepClient? Client;
    public string? RoomCode;
    public MatchStart? Start;
    public Reject? Rejected;
    public PeerLeft? PeerLeft;
    public Queued? InLine;
    public MatchInfo? Info;
    public RatingUpdate? Rated;
    public readonly List<ChatFrom> Heard = new();
    public readonly Random Bot;
    public Action<World>? Sabotage; // test hook: damage this player's own world once, to cause a desync
    public string Auth = ""; // the account session token this player presents
    public byte[]? Kit; // the Armory levels this player announces
    private int _nextThink;

    public TestPlayer(int seed) { Bot = new Random(seed); }

    public void Connect(MatchServer server) => Link.Connect("127.0.0.1", server.Port);

    public void Create(ulong dataHash = 0) => Link.Send(new CreateRoom(MessageCodec.ProtocolVersion, dataHash == 0 ? Defs.DataHash : dataHash, Kit, Auth));
    public void Join(string code, ulong dataHash = 0, int version = MessageCodec.ProtocolVersion)
    {
        Code = code;
        Link.Send(new JoinRoom(version, dataHash == 0 ? Defs.DataHash : dataHash, code, Kit, Auth));
    }

    public void Quick(ulong dataHash = 0) => Link.Send(new QuickMatch(MessageCodec.ProtocolVersion, dataHash == 0 ? Defs.DataHash : dataHash, Kit, Auth));
    public void Say(int preset) => Link.Send(new Chat(preset));
    public void ReportWinner(int team) => Link.Send(new ReportResult(team));

    private static World NewWorld(byte[][] kits)
    {
        World world = OnlineScenario.Create();
        for (int team = 0; team < kits.Length; team++) Armory.FromBytes(kits[team]).Apply(world, team);
        return world;
    }

    /// <summary>Handles everything that arrived. Returns true if anything did.</summary>
    public bool Pump()
    {
        bool any = false;
        while (Link.TryReceive(out NetMessage? message))
        {
            any = true;
            switch (message)
            {
                case RoomCreated created: RoomCode = Code = created.Code; break;
                case PeerBack: PeerCameBack = true; break;
                case MatchResume resume:
                    // Start the world over from nothing: the server now sends every turn played so far.
                    Resumed++;
                    World = NewWorld(resume.Kits);
                    Client = LockstepClient.Resume(World, resume, Link.Send);
                    Client.Start();
                    break;
                case MatchStart start:
                    Start = start;
                    World = NewWorld(start.Kits);
                    Client = new LockstepClient(World, start.Team, start.InputDelayTurns, Link.Send);
                    Client.Start();
                    break;
                case Reject reject: Rejected = reject; break;
                case PeerLeft left: PeerLeft = left; break;
                case Queued queued: InLine = queued; break;
                case MatchInfo info: Info = info; break;
                case RatingUpdate update: Rated = update; break;
                case ChatFrom said: Heard.Add(said); break;
                default: Client?.Receive(message); break;
            }
        }
        return any;
    }

    /// <summary>Plays every tick that can be played right now, giving a random order now and then.</summary>
    public int Play(int targetTicks)
    {
        if (Client == null || World == null) return 0;
        int played = 0;
        while (Client.TicksExecuted < targetTicks)
        {
            if (Client.TicksExecuted >= _nextThink)
            {
                _nextThink = Client.TicksExecuted + 6 + Bot.Next(0, 8);
                Command? c = MatchBot.Next(World, Client.Team, Bot);
                if (c != null) Client.QueueCommand(c);
            }
            if (Sabotage != null && Client.TicksExecuted >= 400)
            {
                Sabotage(World);
                Sabotage = null;
            }
            if (!Client.TryStep()) break;
            played++;
        }
        return played;
    }

    /// <summary>Loses the connection and comes back on a new one with the saved token. With <paramref name="keepOldOpen"/> the old link stays alive (the server has not noticed the loss).</summary>
    public GameConnection Reconnect(MatchServer server, bool keepOldOpen = false, ulong? wrongToken = null)
    {
        GameConnection old = Link;
        if (!keepOldOpen) old.Dispose();
        ulong token = wrongToken ?? Start!.Token;
        Link = new GameConnection();
        Link.Connect("127.0.0.1", server.Port);
        Client = null;
        World = null;
        Link.Send(new Rejoin(MessageCodec.ProtocolVersion, Code!, Start!.Team, token));
        return old;
    }

    public void Dispose() => Link.Dispose();
}

/// <summary>A pretend account server: sessions, ratings, and a record of the results it was told about.</summary>
internal sealed class FakeAccounts : IAccountVerifier
{
    public readonly Dictionary<string, VerifiedPlayer> Sessions = new();
    public readonly Dictionary<long, int> Ratings = new();
    public readonly Dictionary<string, RatedOutcome> Matches = new();
    public bool Down;

    public VerifiedPlayer? Verify(string token) =>
        Sessions.TryGetValue(token, out VerifiedPlayer? p) ? p with { Rating = Ratings.TryGetValue(p.UserId, out int r) ? r : Rating.Start } : null;

    public RatedOutcome? ReportMatch(string matchId, long winnerId, long loserId)
    {
        lock (Matches)
        {
            if (Down) return null;
            if (Matches.TryGetValue(matchId, out RatedOutcome? known)) return known;
            int w = Ratings.TryGetValue(winnerId, out int wr) ? wr : Rating.Start, l = Ratings.TryGetValue(loserId, out int lr) ? lr : Rating.Start;
            Rating.Apply(w, 0, l, 0, out int wn, out int ln);
            Ratings[winnerId] = wn;
            Ratings[loserId] = ln;
            return Matches[matchId] = new RatedOutcome(w, wn, l, ln);
        }
    }
}
