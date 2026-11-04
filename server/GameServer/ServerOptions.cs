namespace GameServer;

public sealed class ServerOptions
{
    /// <summary>0 = pick any free port (used by tests; read the real one from <see cref="MatchServer.Port"/>).</summary>
    public int Port { get; init; } = 7777;

    /// <summary>Where match logs of broken (desynced) games are saved. Null = do not save.</summary>
    public string? LogFolder { get; init; }

    /// <summary>A player who sends nothing for this long is considered gone (clients ping every 2 s).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>A room nobody joined is deleted after this long.</summary>
    public TimeSpan WaitingRoomLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public int InputDelayTurns { get; init; } = 2;
    public int MaxRooms { get; init; } = 1000;

    /// <summary>Messages the server refused (out of order, wrong type...) before it drops the player.</summary>
    public int MaxViolationsPerPlayer { get; init; } = 20;

    /// <summary>Unsent messages waiting for a slow player; beyond this the player is dropped instead of making the server grow.</summary>
    public int MaxOutboxMessages { get; init; } = 200000;

    /// <summary>How long a started match waits for a player who lost the link to come back.</summary>
    public TimeSpan ReconnectGrace { get; init; } = TimeSpan.FromSeconds(60);
}
