using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SimCore
{
    /// <summary>
    /// TCP is a stream of bytes with no message borders, so every message is sent as a 4-byte length (big-endian) followed by that many bytes.
    /// A length that is zero or larger than <see cref="MessageCodec.MaxMessageBytes"/> is a broken or hostile peer: the stream is abandoned, never trusted.
    /// </summary>
    public static class Framing
    {
        public const int MaxFrameBytes = MessageCodec.MaxMessageBytes;

        /// <summary>The frame for a message: length prefix plus payload, ready for one write call.</summary>
        public static byte[] Frame(NetMessage message)
        {
            byte[] payload = MessageCodec.Encode(message);
            var frame = new byte[4 + payload.Length];
            frame[0] = (byte)(payload.Length >> 24);
            frame[1] = (byte)(payload.Length >> 16);
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            return frame;
        }

        /// <summary>Reads one message. Returns null when the other side closed cleanly between messages. Throws <see cref="InvalidDataException"/> on garbage.</summary>
        public static NetMessage Read(Stream stream)
        {
            var header = new byte[4];
            if (!ReadExactly(stream, header, 4, true)) return null;
            int length = ParseLength(header);
            var payload = new byte[length];
            ReadExactly(stream, payload, length, false);
            return Decode(payload);
        }

        public static async Task<NetMessage> ReadAsync(Stream stream, CancellationToken cancel)
        {
            var header = new byte[4];
            if (!await ReadExactlyAsync(stream, header, 4, true, cancel).ConfigureAwait(false)) return null;
            int length = ParseLength(header);
            var payload = new byte[length];
            await ReadExactlyAsync(stream, payload, length, false, cancel).ConfigureAwait(false);
            return Decode(payload);
        }

        private static int ParseLength(byte[] header)
        {
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 1 || length > MaxFrameBytes) throw new InvalidDataException("bad frame length " + length);
            return length;
        }

        private static NetMessage Decode(byte[] payload)
        {
            if (!MessageCodec.TryDecode(payload, out NetMessage message)) throw new InvalidDataException("undecodable message");
            return message;
        }

        // Returns false only when the stream ended before the first byte and that is allowed (a clean close between messages).
        private static bool ReadExactly(Stream stream, byte[] buffer, int count, bool allowCleanEnd)
        {
            int got = 0;
            while (got < count)
            {
                int n = stream.Read(buffer, got, count - got);
                if (n == 0)
                {
                    if (got == 0 && allowCleanEnd) return false;
                    throw new EndOfStreamException();
                }
                got += n;
            }
            return true;
        }

        private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, int count, bool allowCleanEnd, CancellationToken cancel)
        {
            int got = 0;
            while (got < count)
            {
                int n = await stream.ReadAsync(buffer, got, count - got, cancel).ConfigureAwait(false);
                if (n == 0)
                {
                    if (got == 0 && allowCleanEnd) return false;
                    throw new EndOfStreamException();
                }
                got += n;
            }
            return true;
        }
    }
}
