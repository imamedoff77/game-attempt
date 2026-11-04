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
