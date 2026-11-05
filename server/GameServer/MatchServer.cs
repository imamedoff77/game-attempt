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
    private bool Authenticate(Peer peer, string token, byte[] claimedKit, out long accountId, out byte[] kit)
    {
        accountId = 0;
        kit = NetKits.Clean(claimedKit);
        if (_options.Accounts == null) return true;

        VerifiedPlayer? player = token.Length == 0 ? null : _options.Accounts.Verify(token);
        if (player == null)
        {
            peer.Send(new Reject(RejectReason.NotSignedIn));
            return false;
        }
        accountId = player.UserId;
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

        if (!Authenticate(peer, create.AuthToken, create.Kit, out long accountId, out byte[] kit)) return false;

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

        if (!Authenticate(peer, join.AuthToken, join.Kit, out long accountId, out byte[] kit)) return false;

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
            room.Seed = RandomNumberGenerator.GetInt32(int.MaxValue);
            room.InputDelayTurns = _options.InputDelayTurns;
            for (int t = 0; t < World.TeamCount; t++) room.Tokens[t] = NewToken();
            room.Relay = new LockstepRelay((team, msg) => room.Players[team]?.Send(msg));
            room.Started = true;
            Interlocked.Increment(ref MatchesStarted);
            for (int team = 0; team < World.TeamCount; team++)
                room.Players[team]!.Send(new MatchStart(team, room.InputDelayTurns, room.Seed, room.Tokens[team], room.Kits));
        }
        return true;
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
        Room? room = peer.Room;
        if (room == null) return;

        bool remove = false;
        lock (room.Sync)
        {
            if (room.Players[peer.Team] != peer) return; // this seat already belongs to a returning player's new connection
            room.Players[peer.Team] = null;
            foreach (Peer? other in room.Players)
                other?.Send(new PeerLeft(peer.Team));

            if (room.Started && !room.Finished)
            {
                // The match is kept for a while: the player may come back (see HandleRejoin). The cleanup loop gives up after the grace period.
                room.GraceDeadlineUtc ??= DateTime.UtcNow + _options.ReconnectGrace;
            }
            else if (room.PlayerCount == 0) remove = true;
        }
        if (remove) lock (_roomsLock) _rooms.Remove(room.Code);
    }

    // ---- housekeeping ------------------------------------------------------------------------------------------

    private async Task CleanupLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false); }
            catch { return; }

            Room[] all;
            lock (_roomsLock) all = _rooms.Values.ToArray();
            foreach (Room room in all)
            {
                bool remove = false;
                lock (room.Sync)
                {
                    if (!room.Started)
                    {
                        if (DateTime.UtcNow - room.CreatedUtc >= _options.WaitingRoomLifetime) room.Players[0]?.Close();
                    }
                    else if (!room.Finished && room.GraceDeadlineUtc != null && DateTime.UtcNow >= room.GraceDeadlineUtc)
                    {
                        // Nobody came back in time: the match is over for whoever is still here.
                        room.Finished = true;
                        foreach (Peer? p in room.Players) p?.CloseAfterFlush();
                        remove = room.PlayerCount == 0;
                    }
                }
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
