namespace GameServer;

/// <summary>Artificial network delay for testing how the game feels on a bad connection ("lag MS JITTER" typed into the server console).</summary>
public static class Lag
{
    private static int _delayMs, _jitterMs;
    [ThreadStatic] private static Random? _rng;

    public static void Set(int delayMs, int jitterMs)
    {
        _delayMs = Math.Max(0, delayMs);
        _jitterMs = Math.Max(0, jitterMs);
    }

    /// <summary>Milliseconds to hold back one message. Applied in each direction, so the round trip grows by about twice this.</summary>
    public static int NextDelayMs()
    {
        if (_delayMs == 0 && _jitterMs == 0) return 0;
        _rng ??= new Random();
        return _delayMs + (_jitterMs > 0 ? _rng.Next(0, _jitterMs + 1) : 0);
    }
}
