using System.Net.Sockets;
using System.Threading.Channels;
using SimCore;

namespace GameServer;

/// <summary>One connected player: a socket plus a queue of frames waiting to be written (so game logic never waits on the network).</summary>
internal sealed class Peer
{
    private readonly TcpClient _tcp;
    private readonly Channel<(long due, byte[] frame)> _outbox = Channel.CreateUnbounded<(long, byte[])>();
    private readonly int _maxOutbox;
    private int _queued;
    private int _closed;

    public Room? Room;
    public int Team = -1;
    public int Violations;
    /// <summary>True while the player waits in the matchmaking line (guarded by the server's queue lock).</summary>
    public bool Queued;
    /// <summary>When this player last said something (for the chat rate limit).</summary>
    public long LastChatMs = long.MinValue / 2;
    public string Name { get; }

    public Peer(TcpClient tcp, int maxOutbox, string name)
    {
        _tcp = tcp;
        _maxOutbox = maxOutbox;
        Name = name;
    }

    public NetworkStream Stream => _tcp.GetStream();
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Never blocks. Safe to call while holding a room lock.</summary>
    public void Send(NetMessage message)
    {
        if (IsClosed) return;
        if (Interlocked.Increment(ref _queued) > _maxOutbox) { Close(); return; }
        _outbox.Writer.TryWrite((Environment.TickCount64 + Lag.NextDelayMs(), Framing.Frame(message)));
    }

    /// <summary>Writes queued frames until the peer is closed. Run once per peer.</summary>
    public async Task WriteLoopAsync(CancellationToken stop)
    {
        try
        {
            var reader = _outbox.Reader;
            while (await reader.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                while (reader.TryRead(out (long due, byte[] frame) item))
                {
                    Interlocked.Decrement(ref _queued);
                    long wait = item.due - Environment.TickCount64;
                    if (wait > 0) await Task.Delay((int)wait, stop).ConfigureAwait(false); // artificial latency, in order like a real link
                    await Stream.WriteAsync(item.frame, stop).ConfigureAwait(false);
                }
            }
        }
        catch { /* the read side notices the broken link and cleans up */ }
        finally { Close(); }
    }

    /// <summary>Stops accepting new messages but lets the ones already queued be written first (e.g. a final notice).</summary>
    public void CloseAfterFlush()
    {
        _outbox.Writer.TryComplete();
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _outbox.Writer.TryComplete();
        try { _tcp.Close(); } catch { }
    }
}
