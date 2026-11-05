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
        /// <summary>This server only lets signed-in players in, and the account token was missing, expired or wrong.</summary>
        NotSignedIn = 7,
        /// <summary>The same account tried to take both seats of one match.</summary>
        SameAccount = 8,
        /// <summary>Sent to the player who stayed: the opponent did not come back in time, so the match is given up (a walkover win).</summary>
        OpponentGone = 9,
    }

    /// <summary>Client to server: open a room and wait for an opponent. <see cref="DataHash"/> is <c>Defs.DataHash</c>.</summary>
    public sealed class CreateRoom : NetMessage
    {
        public int ProtocolVersion { get; }
        public ulong DataHash { get; }
        /// <summary>The sender's Armory levels (<see cref="Armory.ToBytes"/>). The server cleans them up; see ACCOUNTS_PLAN.md for the missing account check.</summary>
        public byte[] Kit { get; }
        /// <summary>The player's account session token (see server/ACCOUNTS.md); empty for a guest. Checked by the server, never passed on to the opponent.</summary>
        public string AuthToken { get; }

        public CreateRoom(int protocolVersion, ulong dataHash, byte[] kit = null, string authToken = "")
        {
            ProtocolVersion = protocolVersion; DataHash = dataHash; Kit = kit ?? new byte[0]; AuthToken = authToken ?? "";
        }
    }

    /// <summary>Client to server: join the room with this code.</summary>
    public sealed class JoinRoom : NetMessage
    {
        public int ProtocolVersion { get; }
        public ulong DataHash { get; }
        public string Code { get; }
        public byte[] Kit { get; }
        public string AuthToken { get; }

        public JoinRoom(int protocolVersion, ulong dataHash, string code, byte[] kit = null, string authToken = "")
        {
            ProtocolVersion = protocolVersion; DataHash = dataHash; Code = code; Kit = kit ?? new byte[0]; AuthToken = authToken ?? "";
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
        /// <summary>Both players' Armory levels (index = team), checked by the server; both clients apply the same ones, so the games stay identical.</summary>
        public byte[][] Kits { get; }

        public MatchStart(int team, int inputDelayTurns, int seed, ulong token = 0, byte[][] kits = null)
        {
            Team = team; InputDelayTurns = inputDelayTurns; Seed = seed; Token = token; Kits = NetKits.Normalize(kits);
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
        public byte[][] Kits { get; }

        public MatchResume(int team, int inputDelayTurns, int seed, int replayTurns, int nextSubmitTurn, byte[][] kits = null)
        {
            Team = team; InputDelayTurns = inputDelayTurns; Seed = seed; ReplayTurns = replayTurns; NextSubmitTurn = nextSubmitTurn; Kits = NetKits.Normalize(kits);
        }
    }

    /// <summary>Client to server: "find me an opponent of about my strength" (ranked). Same fields as <see cref="CreateRoom"/>; the server answers <see cref="Queued"/> and later <see cref="MatchStart"/>.</summary>
    public sealed class QuickMatch : NetMessage
    {
        public int ProtocolVersion { get; }
        public ulong DataHash { get; }
        public byte[] Kit { get; }
        public string AuthToken { get; }

        public QuickMatch(int protocolVersion, ulong dataHash, byte[] kit = null, string authToken = "")
        {
            ProtocolVersion = protocolVersion; DataHash = dataHash; Kit = kit ?? new byte[0]; AuthToken = authToken ?? "";
        }
    }

    /// <summary>Server to a player who asked for a quick match: you are in the line. <see cref="Rating"/> is the rating the search uses.</summary>
    public sealed class Queued : NetMessage
    {
        public int Rating { get; }

        public Queued(int rating)
        {
            Rating = rating;
        }
    }

    /// <summary>Client to server: stop searching (the connection is closed afterwards).</summary>
    public sealed class LeaveQueue : NetMessage
    {
    }

    /// <summary>
    /// Server to both players right after <see cref="MatchStart"/> of a quick match: it is ranked, and these are the two players' ratings (index = team).
    /// Friend rooms never get one (they are unranked).
    /// </summary>
    public sealed class MatchInfo : NetMessage
    {
        public int[] Ratings { get; }

        public MatchInfo(int[] ratings)
        {
            Ratings = ratings ?? new int[World.TeamCount];
        }
    }

    /// <summary>
    /// Client to server when the match has a winner by the rules (a base fell, or somebody surrendered): "team N won".
    /// The server rates the match when both players say the same, or when only one is left to say it.
    /// </summary>
    public sealed class ReportResult : NetMessage
    {
        public int WinnerTeam { get; }

        public ReportResult(int winnerTeam)
        {
            WinnerTeam = winnerTeam;
        }
    }

    /// <summary>Server to a rated player when their match was counted: the rating before and after.</summary>
    public sealed class RatingUpdate : NetMessage
    {
        public int OldRating { get; }
        public int NewRating { get; }

        public RatingUpdate(int oldRating, int newRating)
        {
            OldRating = oldRating; NewRating = newRating;
        }
    }

    /// <summary>
    /// Client to server: say one of the ready-made phrases (<see cref="ChatPresets"/>) to the opponent. There is no free text on purpose:
    /// nothing to moderate, nothing to translate wrongly. The server limits how often a player may talk.
    /// </summary>
    public sealed class Chat : NetMessage
    {
        public int Preset { get; }

        public Chat(int preset)
        {
            Preset = preset;
        }
    }

    /// <summary>Server to a player: the opponent (<see cref="Team"/>) said phrase <see cref="Preset"/>.</summary>
    public sealed class ChatFrom : NetMessage
    {
        public int Team { get; }
        public int Preset { get; }

        public ChatFrom(int team, int preset)
        {
            Team = team; Preset = preset;
        }
    }

    /// <summary>The ready-made phrases; their texts are in the string table as <c>chat.N</c>.</summary>
    public static class ChatPresets
    {
        public const int Count = 8;
        /// <summary>The least time between two phrases of one player.</summary>
        public const int MinSecondsBetween = 1;
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

namespace SimCore
{
    /// <summary>Armory levels on the wire: one byte per unit kind, a few bytes at most.</summary>
    public static class NetKits
    {
        public const int MaxKitBytes = 32;

        /// <summary>A clean kit: exactly one byte per unit kind, every level within the armory maximum (missing = 0).</summary>
        public static byte[] Clean(byte[] kit) => Armory.FromBytes(kit).ToBytes();

        /// <summary>Always one clean kit for every team.</summary>
        public static byte[][] Normalize(byte[][] kits)
        {
            var result = new byte[World.TeamCount][];
            for (int t = 0; t < result.Length; t++) result[t] = Clean(kits != null && t < kits.Length ? kits[t] : null);
            return result;
        }
    }
}
