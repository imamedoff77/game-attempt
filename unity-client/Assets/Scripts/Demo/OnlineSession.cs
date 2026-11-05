using System;
using System.Threading.Tasks;
using SimCore;
using UnityEngine;

/// <summary>
/// The Unity side of an online match: opens the connection, waits in the lobby, hands the match to a <see cref="LockstepClient"/>,
/// and if the link breaks during a match, keeps trying to get back in (the server keeps the match for a minute and replays it to us).
/// Everything here runs on the main thread except the connect itself (which runs in the background so the screen does not freeze).
/// </summary>
public sealed class OnlineSession
{
    public const int DefaultPort = 7777;
    private const double RetryEverySeconds = 2.0;
    private const double GiveUpAfterSeconds = 60.0;

    public enum Phase { Connecting, Waiting, Playing, Ended }

    /// <summary>One try at connecting: the socket plus what the background thread found out.</summary>
    private sealed class Attempt
    {
        public readonly GameConnection Conn = new GameConnection();
        public volatile bool Connected;
        public volatile string Error;
    }

    private Attempt _attempt;
    private string _host;
    private int _port;
    private NetMessage _firstMessage; // what to say once connected (create / join)

    // Remembered once the match starts, to get back in after a lost link.
    private string _code;
    private ulong _token;
    private double _retryAt, _giveUpAt;

    public Phase State { get; private set; } = Phase.Connecting;
    public string RoomCode { get; private set; }
    public MatchStart Start { get; private set; }
    public LockstepClient Client { get; private set; }

    /// <summary>True while the link is broken and we are trying to return to the match.</summary>
    public bool Reconnecting { get; private set; }

    /// <summary>Why the session ended, or what is going on, in plain words (not translated: shown mostly in the developer panel).</summary>
    public string Status { get; private set; } = "Connecting...";

    public bool OpponentLeft { get; private set; }
    /// <summary>The opponent did not come back in time: the match is given up and this player wins.</summary>
    public bool OpponentAbandoned { get; private set; }

    // ---- network measurements: how long a turn takes to come back from the server (it includes waiting for the opponent) ----
    private readonly System.Collections.Generic.Dictionary<int, double> _sentAt = new System.Collections.Generic.Dictionary<int, double>();
    private readonly System.Collections.Generic.List<float> _rtts = new System.Collections.Generic.List<float>();
    private double _statsWindowStart, _stallSeconds;
    private int _stallWindowCount;
    /// <summary>One line with the last window's numbers, for the developer panel. Also written to the log as "[Net] ...".</summary>
    public string NetStats { get; private set; } = "";

    public static OnlineSession Host(string address, byte[] kit, string authToken) => Begin(address, null, true, kit, authToken);

    public static OnlineSession Join(string address, string code, byte[] kit, string authToken)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!MessageCodec.IsValidCode(code))
            return new OnlineSession { State = Phase.Ended, Status = "Room code looks wrong" };
        return Begin(address, code, false, kit, authToken);
    }

    private static OnlineSession Begin(string address, string code, bool host, byte[] kit, string authToken)
    {
        var session = new OnlineSession();
        ParseAddress(address, out session._host, out session._port);
        session._firstMessage = host
            ? (NetMessage)new CreateRoom(MessageCodec.ProtocolVersion, Defs.DataHash, kit, authToken)
            : new JoinRoom(MessageCodec.ProtocolVersion, Defs.DataHash, code, kit, authToken);
        session._code = code;
        session.StartAttempt();
        return session;
    }

    /// <summary>"192.168.1.5" or "192.168.1.5:7777". Anything unreadable falls back to the default port.</summary>
    public static void ParseAddress(string address, out string host, out int port)
    {
        host = (address ?? "").Trim();
        port = DefaultPort;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host.Substring(colon + 1), out int p) && p > 0 && p < 65536)
        {
            port = p;
            host = host.Substring(0, colon);
        }
    }

    private static string RefusalText(RejectReason reason)
    {
        switch (reason)
        {
            case RejectReason.NotSignedIn: return "Sign in from the Account screen to play online";
            case RejectReason.SameAccount: return "You cannot play against your own account";
            case RejectReason.WrongVersion: return "Update the game to play online";
            default: return "Server refused: " + reason;
        }
    }

    private void StartAttempt()
    {
        var attempt = new Attempt();
        _attempt = attempt;
        string host = _host;
        int port = _port;
        NetMessage first = _firstMessage;
        Task.Run(() =>
        {
            try
            {
                attempt.Conn.Connect(host, port);
                attempt.Conn.Send(first);
                attempt.Connected = true;
            }
            catch (Exception e)
            {
                attempt.Error = e.Message;
            }
        });
    }

    /// <summary>
    /// Handles everything that arrived. Returns a <see cref="MatchStart"/> or a <see cref="MatchResume"/> (once each) when the game
    /// must build a world and call <see cref="AttachWorld"/> / <see cref="AttachResume"/>; null otherwise.
    /// </summary>
    public NetMessage Pump()
    {
        if (State == Phase.Ended) return null;
        double now = Time.realtimeSinceStartupAsDouble;

        if (_attempt == null)
        {
            // Between reconnect attempts.
            if (now >= _giveUpAt) { End("Could not get back into the match"); return null; }
            if (now >= _retryAt) StartAttempt();
            return null;
        }

        Attempt a = _attempt;
        if (a.Error != null)
        {
            if (Reconnecting) { ScheduleRetry(now); return null; }
            End("Could not connect: " + a.Error);
            return null;
        }

        if (Client != null && Client.Waiting && !Reconnecting) _stallSeconds += Time.unscaledDeltaTime;
        if (Client != null && now - _statsWindowStart >= 5.0) PublishStats(now);

        NetMessage begin = null;
        while (begin == null && a.Connected && a.Conn.TryReceive(out NetMessage message))
        {
            switch (message)
            {
                case RoomCreated created:
                    RoomCode = _code = created.Code;
                    State = Phase.Waiting;
                    Status = "Waiting for the opponent. Room code: " + created.Code;
                    break;
                case MatchStart start:
                    Start = start;
                    _token = start.Token;
                    State = Phase.Playing;
                    Status = "Match started. You are team " + start.Team;
                    begin = start;
                    break;
                case MatchResume resume:
                    Reconnecting = false;
                    State = Phase.Playing;
                    Status = "Back in the match. Catching up...";
                    begin = resume;
                    break;
                case Reject reject when reject.Reason == RejectReason.OpponentGone:
                    OpponentAbandoned = true;
                    End("The opponent did not come back");
                    return null;
                case Reject reject:
                    End(Reconnecting ? "The match is over (" + reject.Reason + ")" : RefusalText(reject.Reason));
                    return null;
                case PeerLeft _:
                    OpponentLeft = true;
                    Status = "The opponent lost the connection";
                    break;
                case PeerBack _:
                    OpponentLeft = false;
                    Status = "The opponent is back";
                    break;
                case DesyncNotice notice:
                    Client?.Receive(notice);
                    Status = "OUT OF SYNC at turn " + notice.Turn + " - match stopped";
                    break;
                default:
                    if (message is TurnBundle bundle && _sentAt.TryGetValue(bundle.Turn, out double sent))
                    {
                        _rtts.Add((float)((now - sent) * 1000.0));
                        _sentAt.Remove(bundle.Turn);
                    }
                    Client?.Receive(message);
                    break;
            }
        }

        if (begin == null && a.Conn.Closed)
        {
            if (Client != null && Client.Desynced) { State = Phase.Ended; }
            else if (Start != null && !Reconnecting)
            {
                // Lost the link in the middle of a match: try to get back in.
                Reconnecting = true;
                _giveUpAt = now + GiveUpAfterSeconds;
                Status = "Connection lost, reconnecting...";
                ScheduleRetry(now, immediately: true);
            }
            else if (Reconnecting) ScheduleRetry(now);
            else End("Connection lost: " + a.Conn.Failure);
        }
        return begin;
    }

    private void PublishStats(double now)
    {
        double window = now - _statsWindowStart;
        _statsWindowStart = now;
        if (_rtts.Count > 0)
        {
            _rtts.Sort();
            float sum = 0f;
            foreach (float r in _rtts) sum += r;
            float p95 = _rtts[Mathf.Min(_rtts.Count - 1, (int)(_rtts.Count * 0.95f))];
            NetStats = $"turn rtt avg {sum / _rtts.Count:0} ms, p95 {p95:0}, max {_rtts[_rtts.Count - 1]:0}; stalled {_stallSeconds / window * 100.0:0}% of time";
            Debug.Log($"[Net] rttAvgMs={sum / _rtts.Count:0.0} rttP95Ms={p95:0.0} rttMaxMs={_rtts[_rtts.Count - 1]:0.0} turns={_rtts.Count} stallPct={_stallSeconds / window * 100.0:0.0} tick={Client.TicksExecuted} buffered={Client.TurnsBuffered}");
        }
        _rtts.Clear();
        _stallSeconds = 0;
        if (_sentAt.Count > 200) _sentAt.Clear(); // never grow if answers stop coming
    }

    private void SendTracked(GameConnection conn, NetMessage message)
    {
        if (message is TurnSubmit submit) _sentAt[submit.Turn] = Time.realtimeSinceStartupAsDouble;
        conn.Send(message);
    }

    private void ScheduleRetry(double now, bool immediately = false)
    {
        _attempt?.Conn.Dispose();
        _attempt = null;
        _firstMessage = new Rejoin(MessageCodec.ProtocolVersion, _code, Start.Team, _token);
        _retryAt = immediately ? now : now + RetryEverySeconds;
    }

    /// <summary>Creates the lockstep engine for the world the game has just built from <see cref="Start"/>.</summary>
    public void AttachWorld(World world)
    {
        GameConnection conn = _attempt.Conn;
        Client = new LockstepClient(world, Start.Team, Start.InputDelayTurns, m => SendTracked(conn, m));
        Client.Start();
    }

    /// <summary>The same after a reconnect: <paramref name="world"/> is a fresh starting world that will replay the whole match.</summary>
    public void AttachResume(World world, MatchResume resume)
    {
        GameConnection conn = _attempt.Conn;
        Client = LockstepClient.Resume(world, resume, m => SendTracked(conn, m));
        Client.Start();
    }

    private void End(string status)
    {
        State = Phase.Ended;
        Reconnecting = false;
        Status = status;
        _attempt?.Conn.Dispose();
    }

    public void Leave()
    {
        State = Phase.Ended;
        Reconnecting = false;
        _attempt?.Conn.Dispose();
    }
}
