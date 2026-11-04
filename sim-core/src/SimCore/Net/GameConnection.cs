using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace SimCore
{
    /// <summary>
    /// A client's TCP link to the server. Reading happens on a background thread; the game loop picks messages up with
    /// <see cref="TryReceive"/> on its own thread, so the simulation is never touched from two threads.
    /// While nothing else is being sent a <see cref="Ping"/> goes out every couple of seconds so the server knows we are alive.
    /// </summary>
    public sealed class GameConnection : IDisposable
    {
        public const int PingIntervalMs = 2000;

        private readonly TcpClient _tcp = new TcpClient { NoDelay = true };
        private readonly ConcurrentQueue<NetMessage> _inbox = new ConcurrentQueue<NetMessage>();
        private readonly object _writeLock = new object();
        private Stream _stream;
        private Timer _pinger;
        private long _lastSendTicks;
        private volatile bool _closed;

        private static long Now() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>Null while the link is fine; otherwise why it ended ("closed by server" or the error text).</summary>
        public string Failure { get; private set; }

        public bool Closed => _closed;

        /// <summary>Opens the link and starts listening. Blocks until connected (call it off the game thread, or accept a short freeze on a LAN).</summary>
        public void Connect(string host, int port, int timeoutMs = 5000)
        {
            var connect = _tcp.ConnectAsync(host, port);
            if (!connect.Wait(timeoutMs)) { Close("connect timed out"); throw new TimeoutException("connect timed out"); }
            _stream = _tcp.GetStream();
            _lastSendTicks = Now();
            var reader = new Thread(ReadLoop) { IsBackground = true, Name = "net-read" };
            reader.Start();
            _pinger = new Timer(_ => PingIfIdle(), null, PingIntervalMs, PingIntervalMs);
        }

        public void Send(NetMessage message)
        {
            if (_closed || _stream == null) return;
            byte[] frame = Framing.Frame(message);
            try
            {
                lock (_writeLock)
                {
                    _stream.Write(frame, 0, frame.Length);
                    _lastSendTicks = Now();
                }
            }
            catch (Exception e) { Close(e.Message); }
        }

        public bool TryReceive(out NetMessage message) => _inbox.TryDequeue(out message);

        private void ReadLoop()
        {
            try
            {
                while (!_closed)
                {
                    NetMessage message = Framing.Read(_stream);
                    if (message == null) { Close("closed by server"); return; }
                    _inbox.Enqueue(message);
                }
            }
            catch (Exception e) { Close(e.Message); }
        }

        private void PingIfIdle()
        {
            if (Now() - _lastSendTicks >= PingIntervalMs - 100) Send(new Ping());
        }

        private void Close(string why)
        {
            if (_closed) return;
            if (Failure == null) Failure = why;
            _closed = true;
            try { _pinger?.Dispose(); } catch { }
            try { _tcp.Close(); } catch { }
        }

        public void Dispose() => Close("disposed");
    }
}
