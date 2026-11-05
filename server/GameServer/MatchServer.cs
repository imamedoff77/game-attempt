using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using SimCore;

namespace GameServer;

/// <summary>
/// The relay server: takes TCP connections, pairs players into rooms by code, and lets <see cref="LockstepRelay"/>
/// order their commands. It never simulates the game. The server (not the player) decides who is team 0 and team 1.
/// </summary>
public sealed class MatchServer
{
    // No 0/O or 1/I: codes are read aloud and typed on a phone.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 4;

    private readonly ServerOptions _options;
    private readonly Dictionary<string, Room> _rooms = new();
    private readonly object _roomsLock = new();
    private readonly List<QueueEntry> _queue = new();
    private readonly object _queueLock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _tasks = new();
    private TcpListener? _listener;
    private Task? _acceptTask;

    public MatchServer(ServerOptions options)
    {
        _options = options;
    }

    public int Port { get; private set; }

    public int DesyncsDetected;
    public int MatchesStarted;
    public int Rejoins;
    public int RatedMatchesCounted;
    public int QueueLength { get { lock (_queueLock) return _queue.Count; } }
    public int RoomCount { get { lock (_roomsLock) return _rooms.Count; } }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.IPv6Any, _options.Port) { Server = { DualMode = true } };
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = Task.Run(AcceptLoopAsync);
        _tasks.Add(Task.Run(CleanupLoopAsync));
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        _listener?.Stop();
        lock (_roomsLock)
            foreach (Room room in _rooms.Values)
                lock (room.Sync)
                    foreach (Peer? p in room.Players) p?.Close();
        try { if (_acceptTask != null) await _acceptTask.ConfigureAwait(false); } catch { }
        Task[] running;
        lock (_tasks) running = _tasks.ToArray();
        try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { }
    }

    /// <summary>Cuts a player's connection without any goodbye, as a bad network would. For testing reconnects by hand.</summary>
    public bool DropPlayer(string code, int team)
    {
        Room? room;
        lock (_roomsLock) _rooms.TryGetValue(code.ToUpperInvariant(), out room);
        if (room == null || team < 0 || team >= World.TeamCount) return false;
        Peer? peer;
        lock (room.Sync) peer = room.Players[team];
        peer?.Close();
        return peer != null;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
            catch { return; }

            tcp.NoDelay = true;
            var peer = new Peer(tcp, _options.MaxOutboxMessages, tcp.Client.RemoteEndPoint?.ToString() ?? "?");
            Task task = Task.Run(() => HandlePeerAsync(peer));
            lock (_tasks)
            {
                _tasks.RemoveAll(t => t.IsCompleted);
                _tasks.Add(task);
            }
        }
    }

    private async Task HandlePeerAsync(Peer peer)
    {
        Task writer = Task.Run(() => peer.WriteLoopAsync(_stop.Token));
        // Messages go through a queue so that artificial lag (testing only) delays each one without limiting how fast they can arrive.
        var inbox = System.Threading.Channels.Channel.CreateUnbounded<(long due, NetMessage message)>();
        Task processor = Task.Run(async () =>
        {
            try
            {
                await foreach ((long due, NetMessage message) in inbox.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                {
                    long wait = due - Environment.TickCount64;
                    if (wait > 0) await Task.Delay((int)wait, _stop.Token).ConfigureAwait(false);
                    if (!OnMessage(peer, message)) { peer.CloseAfterFlush(); break; }
                }
            }
            catch { }
            finally { inbox.Writer.TryComplete(); }
        });
        try
        {
            while (!peer.IsClosed && !_stop.IsCancellationRequested)
            {
                NetMessage? message;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                {
                    timeout.CancelAfter(_options.IdleTimeout);
                    message = await Framing.ReadAsync(peer.Stream, timeout.Token).ConfigureAwait(false);
                }
                if (message == null) break; // clean close
                if (!inbox.Writer.TryWrite((Environment.TickCount64 + Lag.NextDelayMs(), message))) break; // processor stopped: the player was refused
            }
        }
        catch
        {
            // timeout, reset connection, garbage on the wire: all mean the same thing, the player is gone
        }
        finally
        {
            inbox.Writer.TryComplete();
            try { await Task.WhenAny(processor, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false); } catch { }
            OnDisconnect(peer);
            // Let a final message (a Reject, say) reach the player before the socket closes, but never wait on a player who stopped reading.
            peer.CloseAfterFlush();
            try { await Task.WhenAny(writer, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false); } catch { }
            peer.Close();
        }
    }

    /// <summary>Returns false when the player must be disconnected.</summary>
    private bool OnMessage(Peer peer, NetMessage message)
    {
        switch (message)
        {
            case Ping:
                return true;
            case CreateRoom create when peer.Room == null:
                return HandleCreate(peer, create);
            case JoinRoom join when peer.Room == null:
                return HandleJoin(peer, join);
            case Rejoin rejoin when peer.Room == null:
                return HandleRejoin(peer, rejoin);
            case QuickMatch quick when peer.Room == null && !peer.Queued:
                return HandleQuick(peer, quick);
            case LeaveQueue when peer.Queued:
                RemoveFromQueue(peer);
                return false; // the connection is not needed any more
            case LeaveQueue when peer.Room != null:
                return true; // too late: the match was found at the same moment
            case Chat chat when peer.Room != null:
                return HandleChat(peer, chat);
            case ReportResult report when peer.Room != null:
                return HandleReport(peer, report);
            case TurnSubmit or HashReport when peer.Room != null:
                return Relay(peer, message);
            default:
                peer.Send(new Reject(RejectReason.BadRequest));
                return false;
        }
    }

    /// <summary>
    /// Works out who the player is and which Armory they may bring. On an open server (no account check) that is the guest with the
    /// levels they claim; with an account check the token must be valid and the claim is cut down to what the account holds.
    /// Sends the refusal itself and returns false when the player may not play.
    /// </summary>
    private bool Authenticate(Peer peer, string token, byte[] claimedKit, out long accountId, out byte[] kit, out int rating)
    {
        accountId = 0;
        rating = Rating.Start;
        kit = NetKits.Clean(claimedKit);
        if (_options.Accounts == null) return true;

        VerifiedPlayer? player = token.Length == 0 ? null : _options.Accounts.Verify(token);
        if (player == null)
        {
            peer.Send(new Reject(RejectReason.NotSignedIn));
            return false;
        }
        accountId = player.UserId;
        rating = player.Rating;
        byte[] owned = NetKits.Clean(player.Kit);
        for (int i = 0; i < kit.Length; i++) kit[i] = Math.Min(kit[i], owned[i]);
        return true;
    }

    private bool HandleCreate(Peer peer, CreateRoom create)
    {
        if (create.ProtocolVersion != MessageCodec.ProtocolVersion)
        {
            peer.Send(new Reject(RejectReason.WrongVersion));
            return false;
        }

        if (!Authenticate(peer, create.AuthToken, create.Kit, out long accountId, out byte[] kit, out _)) return false;

        Room? room = null;
        lock (_roomsLock)
        {
            if (_rooms.Count >= _options.MaxRooms)
            {
                peer.Send(new Reject(RejectReason.ServerBusy));
                return false;
            }
            for (int attempt = 0; attempt < 50 && room == null; attempt++)
            {
                string code = NewCode();
                if (_rooms.ContainsKey(code)) continue;
                room = new Room(code, create.DataHash);
                _rooms[code] = room;
            }
        }
        if (room == null)
        {
            peer.Send(new Reject(RejectReason.ServerBusy));
            return false;
        }

        lock (room.Sync)
        {
            room.Players[0] = peer;
            room.Kits[0] = kit;
            room.AccountIds[0] = accountId;
            peer.Room = room;
            peer.Team = 0;
        }
        peer.Send(new RoomCreated(room.Code));
        return true;
    }

    private bool HandleJoin(Peer peer, JoinRoom join)
    {
        if (join.ProtocolVersion != MessageCodec.ProtocolVersion)
        {
            peer.Send(new Reject(RejectReason.WrongVersion));
            return false;
        }

        if (!Authenticate(peer, join.AuthToken, join.Kit, out long accountId, out byte[] kit, out _)) return false;

        Room? room;
        lock (_roomsLock) _rooms.TryGetValue(join.Code, out room);
        if (room == null)
        {
            peer.Send(new Reject(RejectReason.NoSuchRoom));
            return false;
        }

        lock (room.Sync)
        {
            if (room.Finished || room.Players[1] != null || room.Players[0] == null)
            {
                peer.Send(new Reject(room.Finished ? RejectReason.NoSuchRoom : RejectReason.RoomFull));
                return false;
            }
            if (join.DataHash != room.DataHash)
            {
                peer.Send(new Reject(RejectReason.WrongData));
                return false;
            }

            if (accountId != 0 && room.AccountIds[0] == accountId)
            {
                peer.Send(new Reject(RejectReason.SameAccount));
                return false;
            }

            room.Players[1] = peer;
            room.Kits[1] = kit;
            room.AccountIds[1] = accountId;
            peer.Room = room;
            peer.Team = 1;
            BeginMatch(room);
        }
        return true;
    }

    /// <summary>Both seats are taken: set the match up and tell both players. Call while holding <c>room.Sync</c>.</summary>
    private void BeginMatch(Room room)
    {
        room.Seed = RandomNumberGenerator.GetInt32(int.MaxValue);
        room.InputDelayTurns = _options.InputDelayTurns;
        for (int t = 0; t < World.TeamCount; t++) room.Tokens[t] = NewToken();
        room.Relay = new LockstepRelay((team, msg) => room.Players[team]?.Send(msg));
        room.Started = true;
        Interlocked.Increment(ref MatchesStarted);
        for (int team = 0; team < World.TeamCount; team++)
            room.Players[team]!.Send(new MatchStart(team, room.InputDelayTurns, room.Seed, room.Tokens[team], room.Kits));
        if (room.Ranked)
            for (int team = 0; team < World.TeamCount; team++) room.Players[team]!.Send(new MatchInfo(room.Ratings));
    }

    // ---- matchmaking ---------------------------------------------------------------------------------------------

    private sealed class QueueEntry
    {
        public Peer Peer = null!;
        public long AccountId;
        public int Rating;
        public byte[] Kit = null!;
        public ulong DataHash;
        public DateTime SinceUtc;
    }

    private bool HandleQuick(Peer peer, QuickMatch quick)
    {
        if (quick.ProtocolVersion != MessageCodec.ProtocolVersion)
        {
            peer.Send(new Reject(RejectReason.WrongVersion));
            return false;
        }
        if (_options.VerifyResults && quick.DataHash != Defs.DataHash)
        {
            peer.Send(new Reject(RejectReason.WrongData)); // the server could not replay this player's matches
            return false;
        }
        if (!Authenticate(peer, quick.AuthToken, quick.Kit, out long accountId, out byte[] kit, out int rating)) return false;

        lock (_queueLock)
        {
            if (_queue.Count >= _options.MaxRooms)
            {
                peer.Send(new Reject(RejectReason.ServerBusy));
                return false;
            }
            if (accountId != 0 && _queue.Any(e => e.AccountId == accountId))
            {
                peer.Send(new Reject(RejectReason.SameAccount));
                return false;
            }
            peer.Queued = true;
            _queue.Add(new QueueEntry { Peer = peer, AccountId = accountId, Rating = rating, Kit = kit, DataHash = quick.DataHash, SinceUtc = DateTime.UtcNow });
        }
        peer.Send(new Queued(rating));
        TryMatchmake();
        return true;
    }

    private void RemoveFromQueue(Peer peer)
    {
        lock (_queueLock)
        {
            if (!peer.Queued) return;
            peer.Queued = false;
            _queue.RemoveAll(e => e.Peer == peer);
        }
    }

    /// <summary>Pairs waiting players: the closest ratings first, and the longer somebody waits the further apart their ratings may be.</summary>
    private void TryMatchmake()
    {
        var pairs = new List<(QueueEntry, QueueEntry)>();
        lock (_queueLock)
        {
            foreach (QueueEntry gone in _queue.Where(e => e.Peer.IsClosed).ToList()) { gone.Peer.Queued = false; _queue.Remove(gone); }
            DateTime now = DateTime.UtcNow;
            bool found;
            do
            {
                found = false;
                for (int i = 0; i < _queue.Count && !found; i++)
                {
                    QueueEntry a = _queue[i];
                    int best = -1, bestDiff = int.MaxValue;
                    for (int j = i + 1; j < _queue.Count; j++)
                    {
                        QueueEntry b = _queue[j];
                        if (b.DataHash != a.DataHash || (a.AccountId != 0 && a.AccountId == b.AccountId)) continue;
                        int diff = Math.Abs(a.Rating - b.Rating);
                        double waited = Math.Max((now - a.SinceUtc).TotalSeconds, (now - b.SinceUtc).TotalSeconds);
                        if (diff <= _options.QueueWindowBase + _options.QueueWindowPerSecond * waited && diff < bestDiff) { best = j; bestDiff = diff; }
                    }
                    if (best < 0) continue;
                    QueueEntry partner = _queue[best];
                    _queue.RemoveAt(best);
                    _queue.RemoveAt(i);
                    a.Peer.Queued = false;
                    partner.Peer.Queued = false;
                    pairs.Add((a, partner));
                    found = true;
                }
            } while (found);
        }
        foreach ((QueueEntry a, QueueEntry b) in pairs) StartQuickMatch(a, b);
    }

    private void StartQuickMatch(QueueEntry a, QueueEntry b)
    {
        Room? room = null;
        lock (_roomsLock)
        {
            if (_rooms.Count < _options.MaxRooms)
                for (int attempt = 0; attempt < 50 && room == null; attempt++)
                {
                    string code = NewCode();
                    if (_rooms.ContainsKey(code)) continue;
                    room = new Room(code, a.DataHash);
                    _rooms[code] = room;
                }
        }
        if (room == null)
        {
            foreach (QueueEntry e in new[] { a, b }) { e.Peer.Send(new Reject(RejectReason.ServerBusy)); e.Peer.CloseAfterFlush(); }
            return;
        }

        // Who plays which side is a coin toss, so the first one in the line has no advantage.
        QueueEntry[] seats = RandomNumberGenerator.GetInt32(2) == 0 ? new[] { a, b } : new[] { b, a };
        lock (room.Sync)
        {
            room.Ranked = a.AccountId != 0 && b.AccountId != 0;
            room.MatchId = Guid.NewGuid().ToString("N");
            for (int team = 0; team < World.TeamCount; team++)
            {
                room.Players[team] = seats[team].Peer;
                room.Kits[team] = new Armory().ToBytes(); // quick matches are played on equal terms: no Armory bonuses for anybody
                room.AccountIds[team] = seats[team].AccountId;
                room.Ratings[team] = seats[team].Rating;
                seats[team].Peer.Room = room;
                seats[team].Peer.Team = team;
            }
            BeginMatch(room);
        }
        // A player who vanished while being matched is handled like one who lost the link: the other waits out the grace period.
        foreach (QueueEntry e in seats) if (e.Peer.IsClosed) OnDisconnect(e.Peer);
    }

    /// <summary>A ready-made phrase for the opponent. Too frequent phrases are dropped (not a reason to disconnect anybody).</summary>
    private bool HandleChat(Peer peer, Chat chat)
    {
        Room room = peer.Room!;
        long now = Environment.TickCount64;
        if (now - peer.LastChatMs < ChatPresets.MinSecondsBetween * 1000L) return true;
        peer.LastChatMs = now;
        lock (room.Sync)
        {
            if (!room.Started) return true;
            room.Players[1 - peer.Team]?.Send(new ChatFrom(peer.Team, chat.Preset));
        }
        return true;
    }

    // ---- results -------------------------------------------------------------------------------------------------

    private bool HandleReport(Peer peer, ReportResult report)
    {
        Room room = peer.Room!;
        int winner = -1;
        bool verify = false;
        lock (room.Sync)
        {
            if (!room.Ranked || !room.Started || room.Resolved || room.Reports[peer.Team] >= 0) return true; // friend rooms are not rated; only the first word counts
            room.Reports[peer.Team] = report.WinnerTeam;
            room.FirstReportUtc ??= DateTime.UtcNow;
            if (_options.VerifyResults) verify = true;
            else winner = ResolveLocked(room);
        }
        if (verify) _ = Task.Run(() => VerifyClaim(room, peer.Team, report.WinnerTeam));
        else if (winner >= 0) CountResult(room, winner, walkover: false, verified: true);
        return true;
    }

    /// <summary>A player says who won: the replay of the recorded match decides. A claim the replay does not back up is ignored.</summary>
    private void VerifyClaim(Room room, int claimant, int claim)
    {
        int verdict = ReplayVerdict(room);
        if (verdict < 0)
        {
            Console.Error.WriteLine($"room {room.Code}: team {claimant} says team {claim} won, but the replay shows no winner ({(verdict == MatchReferee.Draw ? "draw" : "match still running")}); not rated");
            return;
        }
        bool first;
        lock (room.Sync)
        {
            first = !room.Resolved;
            room.Resolved = true;
        }
        if (verdict != claim) Console.Error.WriteLine($"room {room.Code}: team {claimant} claimed team {claim}, the replay says team {verdict}");
        if (first) CountResult(room, verdict, walkover: false, verified: true);
    }

    private static int ReplayVerdict(Room room)
    {
        List<TurnBundle> log;
        byte[][] kits;
        lock (room.Sync)
        {
            if (room.Relay == null) return MatchReferee.Ongoing;
            log = room.Relay.Log.ToList();
            kits = new[] { (byte[])room.Kits[0].Clone(), (byte[])room.Kits[1].Clone() };
        }
        try { return Replay.Verdict(log, kits); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"room {room.Code}: the replay failed: {e.Message}");
            return MatchReferee.Ongoing;
        }
    }

    /// <summary>
    /// Decides the result of a ranked room from what the players said. Returns the winning team, or -1 when it is not decided yet or was thrown out.
    /// Both agree: counted. They disagree: nothing counted (and noted in the log). One spoke and the other is gone: the one who spoke is believed.
    /// Call while holding <c>room.Sync</c>.
    /// </summary>
    private int ResolveLocked(Room room)
    {
        if (_options.VerifyResults || !room.Ranked || !room.Started || room.Resolved) return -1; // with the replay check, claims are judged by VerifyClaim instead
        int r0 = room.Reports[0], r1 = room.Reports[1];
        if (r0 >= 0 && r1 >= 0)
        {
            room.Resolved = true;
            if (r0 == r1) return r0;
            Console.Error.WriteLine($"room {room.Code}: the players disagree about who won ({r0} / {r1}); match {room.MatchId} is not rated");
            return -1;
        }
        int spoke = r0 >= 0 ? 0 : 1;
        if (room.Reports[spoke] >= 0 && room.Players[1 - spoke] == null)
        {
            room.Resolved = true;
            return room.Reports[spoke];
        }
        return -1;
    }

    /// <summary>
    /// Tells the account server who won (off the room lock, it is a web request) and then both players their new ratings.
    /// A walkover (<paramref name="walkover"/>) is a win for whoever stayed unless the replay shows the match was already decided another way.
    /// </summary>
    private void CountResult(Room room, int winnerTeam, bool walkover, bool verified)
    {
        string matchId = room.MatchId;
        _ = Task.Run(() =>
        {
            if (!verified && _options.VerifyResults)
            {
                int verdict = ReplayVerdict(room);
                if (verdict >= 0) winnerTeam = verdict;
                else if (verdict == MatchReferee.Draw || !walkover)
                {
                    Console.Error.WriteLine($"room {room.Code}: match {matchId} is not rated (replay: {(verdict == MatchReferee.Draw ? "draw" : "no winner")})");
                    FinishAfterResult(room);
                    return;
                }
            }
            long winnerId = room.AccountIds[winnerTeam], loserId = room.AccountIds[1 - winnerTeam];
            RatedOutcome? outcome;
            try { outcome = _options.Accounts?.ReportMatch(matchId, winnerId, loserId); }
            catch (Exception e) { Console.Error.WriteLine("could not count match " + matchId + ": " + e.Message); outcome = null; }
            if (outcome == null)
            {
                Console.Error.WriteLine("the account server did not take the result of match " + matchId);
                FinishAfterResult(room);
                return;
            }
            Interlocked.Increment(ref RatedMatchesCounted);
            lock (room.Sync)
            {
                room.Players[winnerTeam]?.Send(new RatingUpdate(outcome.WinnerOld, outcome.WinnerNew));
                room.Players[1 - winnerTeam]?.Send(new RatingUpdate(outcome.LoserOld, outcome.LoserNew));
            }
            FinishAfterResult(room);
        });
    }

    /// <summary>After a walkover the stayer was kept connected only to hear the result: now let them go.</summary>
    private static void FinishAfterResult(Room room)
    {
        lock (room.Sync) if (room.Finished) foreach (Peer? p in room.Players) p?.CloseAfterFlush();
    }

    /// <summary>A player whose link broke comes back: they get the whole match so far and play on from the live turn.</summary>
    private bool HandleRejoin(Peer peer, Rejoin rejoin)
    {
        if (rejoin.ProtocolVersion != MessageCodec.ProtocolVersion)
        {
            peer.Send(new Reject(RejectReason.WrongVersion));
            return false;
        }

        Room? room;
        lock (_roomsLock) _rooms.TryGetValue(rejoin.Code, out room);

        Peer? stale = null;
        lock (room?.Sync ?? new object())
        {
            if (room == null || !room.Started || room.Finished || room.Relay == null)
            {
                peer.Send(new Reject(RejectReason.NoSuchRoom));
                return false;
            }
            if (room.Tokens[rejoin.Team] != rejoin.Token)
            {
                peer.Send(new Reject(RejectReason.BadRequest));
                return false;
            }

            // The old connection may still look alive to the server (the phone vanished from the network): the token proves this is the same player.
            stale = room.Players[rejoin.Team];
            room.Players[rejoin.Team] = peer;
            peer.Room = room;
            peer.Team = rejoin.Team;

            LockstepRelay relay = room.Relay;
            peer.Send(new MatchResume(rejoin.Team, room.InputDelayTurns, room.Seed, relay.TurnsBundled, relay.NextExpectedTurn(rejoin.Team), room.Kits));
            foreach (TurnBundle bundle in relay.Log) peer.Send(bundle);
            room.Players[1 - rejoin.Team]?.Send(new PeerBack(rejoin.Team));
            if (room.PlayerCount == World.TeamCount) room.GraceDeadlineUtc = null;
        }
        stale?.Close();
        Interlocked.Increment(ref Rejoins);
        return true;
    }

    private bool Relay(Peer peer, NetMessage message)
    {
        Room room = peer.Room!;
        bool desyncedNow = false;
        lock (room.Sync)
        {
            if (!room.Started || room.Finished || room.Relay == null) return false;
            int before = room.Relay.Violations;
            bool wasDesynced = room.Relay.Desynced;
            room.Relay.Receive(peer.Team, message); // the team is the slot the server gave, never anything the client claims
            peer.Violations += room.Relay.Violations - before;
            if (peer.Violations > _options.MaxViolationsPerPlayer) return false;

            if (!wasDesynced && room.Relay.Desynced)
            {
                desyncedNow = true;
                room.Finished = true;
                SaveDesyncLog(room);
            }
        }
        if (desyncedNow)
        {
            Interlocked.Increment(ref DesyncsDetected);
            // The notice is queued for both players already; closing waits for the writers to flush it (Peer.Close completes the queue).
            lock (room.Sync)
                foreach (Peer? p in room.Players) p?.CloseAfterFlush();
        }
        return true;
    }

    private void OnDisconnect(Peer peer)
    {
        RemoveFromQueue(peer);
        Room? room = peer.Room;
        if (room == null) return;

        bool remove = false;
        int winner = -1;
        lock (room.Sync)
        {
            if (room.Players[peer.Team] != peer) return; // this seat already belongs to a returning player's new connection
            room.Players[peer.Team] = null;
            foreach (Peer? other in room.Players)
                other?.Send(new PeerLeft(peer.Team));
            winner = ResolveLocked(room);

            if (room.Started && !room.Finished && !room.Resolved)
            {
                // The match is kept for a while: the player may come back (see HandleRejoin). The cleanup loop gives up after the grace period.
                room.GraceDeadlineUtc ??= DateTime.UtcNow + _options.ReconnectGrace;
            }
            else if (room.PlayerCount == 0) remove = true;
        }
        if (winner >= 0) CountResult(room, winner, walkover: false, verified: true);
        if (remove) lock (_roomsLock) _rooms.Remove(room.Code);
    }

    // ---- housekeeping ------------------------------------------------------------------------------------------

    private async Task CleanupLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false); }
            catch { return; }

            TryMatchmake();

            Room[] all;
            lock (_roomsLock) all = _rooms.Values.ToArray();
            foreach (Room room in all)
            {
                bool remove = false;
                int winner = -1;
                bool walkover = false;
                lock (room.Sync)
                {
                    // One player said who won and the other neither agrees nor leaves: after a while the one who spoke is believed
                    // (with the replay check this is not needed: every claim is judged on arrival).
                    if (!_options.VerifyResults && room.Ranked && !room.Resolved && room.FirstReportUtc != null && DateTime.UtcNow - room.FirstReportUtc >= _options.ReportWait)
                    {
                        int spoke = room.Reports[0] >= 0 ? 0 : 1;
                        if (room.Reports[1 - spoke] < 0) { room.Resolved = true; winner = room.Reports[spoke]; }
                    }
                    if (!room.Started)
                    {
                        if (DateTime.UtcNow - room.CreatedUtc >= _options.WaitingRoomLifetime) room.Players[0]?.Close();
                    }
                    else if (!room.Finished && !room.Resolved && room.GraceDeadlineUtc != null && DateTime.UtcNow >= room.GraceDeadlineUtc)
                    {
                        // Nobody came back in time: the match is over for whoever is still here.
                        room.Finished = true;
                        if (room.Ranked)
                        {
                            // A walkover is a win for the one who stayed (if somebody did).
                            int stayed = room.Players[0] != null ? 0 : room.Players[1] != null ? 1 : -1;
                            if (stayed >= 0) { room.Resolved = true; winner = stayed; walkover = true; }
                        }
                        // The one who stayed is kept connected while a ranked result is on its way (CountResult closes them after the rating update).
                        foreach (Peer? p in room.Players) { p?.Send(new Reject(RejectReason.OpponentGone)); if (winner < 0) p?.CloseAfterFlush(); }
                        remove = room.PlayerCount == 0;
                    }
                }
                if (winner >= 0) CountResult(room, winner, walkover, verified: !walkover);
                if (remove) lock (_roomsLock) _rooms.Remove(room.Code);
            }
        }
    }

    private void SaveDesyncLog(Room room)
    {
        if (_options.LogFolder == null || room.Relay == null) return;
        try
        {
            Directory.CreateDirectory(_options.LogFolder);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string basePath = Path.Combine(_options.LogFolder, $"desync-{room.Code}-{stamp}");

            DesyncNotice info = room.Relay.DesyncInfo;
            File.WriteAllText(basePath + ".txt",
                $"room {room.Code}\nturn {info.Turn}\nhash team0 {info.Hash0:X16}\nhash team1 {info.Hash1:X16}\n" +
                $"data hash {room.DataHash:X16}\nturns relayed {room.Relay.TurnsBundled}\n");

            // The whole command log, one frame per turn: enough to replay the match and find the first tick that went different.
            using var file = File.Create(basePath + ".turns");
            foreach (TurnBundle bundle in room.Relay.Log)
            {
                byte[] frame = Framing.Frame(bundle);
                file.Write(frame, 0, frame.Length);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("could not save desync log: " + e.Message);
        }
    }

    private static ulong NewToken() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8), 0);

    private static string NewCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }
}
