using System.Collections.Generic;

namespace SimCore
{
    /// <summary>Everything two players and the relay server say to each other during a match.</summary>
    public abstract class NetMessage
    {
    }

    /// <summary>Client to server: "these are my commands for turn N" (possibly none). Turns are sent in order, one by one.</summary>
    public sealed class TurnSubmit : NetMessage
    {
        public int Turn { get; }
        public IReadOnlyList<Command> Commands { get; }

        public TurnSubmit(int turn, IReadOnlyList<Command> commands)
        {
            Turn = turn; Commands = commands;
        }
    }

    /// <summary>
    /// Server to both clients: everybody's commands for turn N. Index = team that sent them.
    /// The server decides the team, so a player cannot give orders in someone else's name.
    /// </summary>
    public sealed class TurnBundle : NetMessage
    {
        public int Turn { get; }
        public IReadOnlyList<Command>[] ByTeam { get; }

        public TurnBundle(int turn, IReadOnlyList<Command>[] byTeam)
        {
            Turn = turn; ByTeam = byTeam;
        }
    }

    /// <summary>Client to server: fingerprint of the world after turn N was played. Both must match.</summary>
    public sealed class HashReport : NetMessage
    {
        public int Turn { get; }
        public ulong Hash { get; }

        public HashReport(int turn, ulong hash)
        {
            Turn = turn; Hash = hash;
        }
    }

    /// <summary>Server to both clients: the two fingerprints for turn N differed; the match cannot continue.</summary>
    public sealed class DesyncNotice : NetMessage
    {
        public int Turn { get; }
        public ulong Hash0 { get; }
        public ulong Hash1 { get; }

        public DesyncNotice(int turn, ulong hash0, ulong hash1)
        {
            Turn = turn; Hash0 = hash0; Hash1 = hash1;
        }
    }
}

namespace SimCore
{
    /// <summary>Why the server turned a player away.</summary>
    public enum RejectReason : byte
    {
        NoSuchRoom = 1,
        RoomFull = 2,
        WrongVersion = 3,
        /// <summary>The two players' balance data (gamedata.json) differ: they would see different games.</summary>
        WrongData = 4,
        ServerBusy = 5,
        BadRequest = 6,
    }

    /// <summary>Client to server: open a room and wait for an opponent. <see cref="DataHash"/> is <c>Defs.DataHash</c>.</summary>
    public sealed class CreateRoom : NetMessage
    {
        public int ProtocolVersion { get; }
        public ulong DataHash { get; }

        public CreateRoom(int protocolVersion, ulong dataHash)
        {
            ProtocolVersion = protocolVersion; DataHash = dataHash;
        }
    }

    /// <summary>Client to server: join the room with this code.</summary>
    public sealed class JoinRoom : NetMessage
    {
        public int ProtocolVersion { get; }
        public ulong DataHash { get; }
        public string Code { get; }

        public JoinRoom(int protocolVersion, ulong dataHash, string code)
        {
            ProtocolVersion = protocolVersion; DataHash = dataHash; Code = code;
        }
    }

    /// <summary>Server to the room's creator: share this code with your opponent.</summary>
    public sealed class RoomCreated : NetMessage
    {
        public string Code { get; }

        public RoomCreated(string code)
        {
            Code = code;
        }
    }

    /// <summary>Server to both players: the match begins. You are <see cref="Team"/>; every client sends turns through <see cref="InputDelayTurns"/> turns of delay.</summary>
    public sealed class MatchStart : NetMessage
    {
        public int Team { get; }
        public int InputDelayTurns { get; }
        /// <summary>Same for both players: for anything random that must be shared at the start (map choice).</summary>
        public int Seed { get; }
        /// <summary>A secret only this player knows: it proves who they are when they reconnect after losing the link.</summary>
        public ulong Token { get; }

        public MatchStart(int team, int inputDelayTurns, int seed, ulong token = 0)
        {
            Team = team; InputDelayTurns = inputDelayTurns; Seed = seed; Token = token;
        }
    }

    public sealed class Reject : NetMessage
    {
        public RejectReason Reason { get; }

        public Reject(RejectReason reason)
        {
            Reason = reason;
        }
    }

    /// <summary>Server to the remaining player: the opponent's connection is gone.</summary>
    public sealed class PeerLeft : NetMessage
    {
        public int Team { get; }

        public PeerLeft(int team)
        {
            Team = team;
        }
    }

    /// <summary>Client to server: "still here". Sent while nothing else is being sent (waiting in a room).</summary>
    public sealed class Ping : NetMessage
    {
    }

    /// <summary>Client to server: "I was playing in this room and lost the link, let me back in" (see <see cref="MatchStart.Token"/>).</summary>
    public sealed class Rejoin : NetMessage
    {
        public int ProtocolVersion { get; }
        public string Code { get; }
        public int Team { get; }
        public ulong Token { get; }

        public Rejoin(int protocolVersion, string code, int team, ulong token)
        {
            ProtocolVersion = protocolVersion; Code = code; Team = team; Token = token;
        }
    }

    /// <summary>
    /// Server to a returning player: start the match over from turn 0 with the same settings. The next <see cref="ReplayTurns"/> messages are
    /// the turns already played (replay them fast), and your own turns are already submitted up to <see cref="NextSubmitTurn"/> - 1.
    /// </summary>
    public sealed class MatchResume : NetMessage
    {
        public int Team { get; }
        public int InputDelayTurns { get; }
        public int Seed { get; }
        public int ReplayTurns { get; }
        public int NextSubmitTurn { get; }

        public MatchResume(int team, int inputDelayTurns, int seed, int replayTurns, int nextSubmitTurn)
        {
            Team = team; InputDelayTurns = inputDelayTurns; Seed = seed; ReplayTurns = replayTurns; NextSubmitTurn = nextSubmitTurn;
        }
    }

    /// <summary>Server to the remaining player: the opponent is connected again.</summary>
    public sealed class PeerBack : NetMessage
    {
        public int Team { get; }

        public PeerBack(int team)
        {
            Team = team;
        }
    }
}
