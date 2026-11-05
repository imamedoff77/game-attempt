using SimCore;

namespace GameServer;

/// <summary>One match. All access happens under <see cref="Sync"/>.</summary>
internal sealed class Room
{
    public readonly object Sync = new();
    public readonly string Code;
    public readonly ulong DataHash;
    public readonly DateTime CreatedUtc = DateTime.UtcNow;
    public readonly Peer?[] Players = new Peer?[World.TeamCount];
    public LockstepRelay? Relay;
    public bool Started;
    public bool Finished;
    public int Seed;
    public int InputDelayTurns;
    /// <summary>Each team's cleaned-up Armory levels, as sent when they created or joined the room.</summary>
    public readonly byte[][] Kits = { new byte[0], new byte[0] };
    /// <summary>The account id in each seat (0 = guest or an open server).</summary>
    public readonly long[] AccountIds = new long[World.TeamCount];
    /// <summary>A quick match (found by the matchmaker, not by code): its result changes the ratings.</summary>
    public bool Ranked;
    public string MatchId = "";
    public readonly int[] Ratings = new int[World.TeamCount];
    /// <summary>What each team said at the end of the match (-1 = nothing yet): the winning team.</summary>
    public readonly int[] Reports = { -1, -1 };
    /// <summary>The result was decided (counted, or thrown out because the players disagreed); nothing more is decided for this room.</summary>
    public bool Resolved;
    public DateTime? FirstReportUtc;
    public readonly ulong[] Tokens = new ulong[World.TeamCount];
    /// <summary>Set while a seat is empty in a started match: after this moment the match is given up.</summary>
    public DateTime? GraceDeadlineUtc;

    public Room(string code, ulong dataHash)
    {
        Code = code;
        DataHash = dataHash;
    }

    public int PlayerCount => Players.Count(p => p != null);
}
