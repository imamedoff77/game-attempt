using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// Turns commands into bytes and back, for the network. The layout is fixed and the same on every device:
    /// whole numbers only, little-endian, no padding. A command's <see cref="Command.Team"/> is NOT sent:
    /// the server decides who sent it.
    /// Reading never throws: bad, cut-off or oversized data is simply refused (a malicious client must not be able to crash a server or a peer).
    /// </summary>
    public static class CommandCodec
    {
        public const int MaxUnitsPerCommand = 256;
        public const int MaxCommandsPerTurn = 64;
        /// <summary>Bytes of the biggest possible single command.</summary>
        public const int MaxCommandBytes = 1 + 2 + MaxUnitsPerCommand * 4 + 4 + 8 + 8;
        /// <summary>Map coordinates beyond this are never valid (keeps later arithmetic far from overflow).</summary>
        private const int MaxCoordinate = 4096;

        private const byte TypeMove = 1, TypeBuild = 2, TypeQueue = 3, TypeCancel = 4, TypeRally = 5, TypeAttack = 6, TypeCancelBuild = 7, TypeUpgradeBase = 8, TypeResearch = 9, TypeSurrender = 10;

        // ---------------------------------------------------------------- one command

        public static byte[] Encode(Command command)
        {
            var w = new ByteWriter();
            Write(w, command);
            return w.ToArray();
        }

        /// <summary>Reads exactly one command that fills the whole array. False when anything is wrong.</summary>
        public static bool TryDecode(byte[] data, out Command command)
        {
            command = null;
            if (data == null) return false;
            var r = new ByteReader(data, 0, data.Length);
            return TryRead(r, out command) && r.Remaining == 0;
        }

        // ---------------------------------------------------------------- a turn's worth of commands

        /// <summary>Count, then each command with its own length, so one bad command cannot hide inside the next.</summary>
        public static byte[] EncodeList(IReadOnlyList<Command> commands)
        {
            if (commands.Count > MaxCommandsPerTurn) throw new ArgumentException("too many commands for one turn");
            var w = new ByteWriter();
            w.WriteU16((ushort)commands.Count);
            foreach (Command c in commands)
            {
                byte[] one = Encode(c);
                w.WriteU16((ushort)one.Length);
                w.WriteBytes(one);
            }
            return w.ToArray();
        }

        public static bool TryDecodeList(byte[] data, int offset, int length, out List<Command> commands)
        {
            commands = null;
            if (data == null || offset < 0 || length < 0 || offset > data.Length - length) return false;
            var r = new ByteReader(data, offset, offset + length);
            if (!r.TryReadU16(out ushort count) || count > MaxCommandsPerTurn) return false;
            var list = new List<Command>(count);
            for (int i = 0; i < count; i++)
            {
                if (!r.TryReadU16(out ushort size) || size > r.Remaining || size > MaxCommandBytes) return false;
                var inner = new ByteReader(data, r.Position, r.Position + size);
                if (!TryRead(inner, out Command c) || inner.Remaining != 0) return false;
                r.Skip(size);
                list.Add(c);
            }
            if (r.Remaining != 0) return false;
            commands = list;
            return true;
        }

        // ---------------------------------------------------------------- writing

        private static void Write(ByteWriter w, Command command)
        {
            switch (command)
            {
                case MoveCommand m:
                    w.WriteByte(TypeMove);
                    WriteIds(w, m.UnitIds);
                    w.WriteVec(m.Target);
                    break;
                case BuildCommand b:
                    w.WriteByte(TypeBuild);
                    w.WriteByte((byte)b.Kind);
                    w.WriteI32(b.X);
                    w.WriteI32(b.Y);
                    break;
                case QueueUnitCommand q:
                    w.WriteByte(TypeQueue);
                    w.WriteI32(q.BuildingId);
                    w.WriteByte((byte)q.Kind);
                    break;
                case CancelQueuedCommand c:
                    w.WriteByte(TypeCancel);
                    w.WriteI32(c.BuildingId);
                    w.WriteI32(c.Index);
                    break;
                case SetRallyCommand s:
                    w.WriteByte(TypeRally);
                    w.WriteI32(s.BuildingId);
                    w.WriteVec(s.Point);
                    break;
                case AttackCommand a:
                    w.WriteByte(TypeAttack);
                    WriteIds(w, a.UnitIds);
                    w.WriteByte((byte)a.TargetKind);
                    w.WriteI32(a.TargetId);
                    break;
                case CancelBuildCommand cb:
                    w.WriteByte(TypeCancelBuild);
                    w.WriteI32(cb.BuildingId);
                    break;
                case UpgradeBaseCommand _:
                    w.WriteByte(TypeUpgradeBase);
                    break;
                case ResearchCommand rc:
                    w.WriteByte(TypeResearch);
                    w.WriteByte((byte)rc.Kind);
                    break;
                case SurrenderCommand _:
                    w.WriteByte(TypeSurrender);
                    break;
                default:
                    throw new ArgumentException("unknown command type " + (command == null ? "null" : command.GetType().Name));
            }
        }

        private static void WriteIds(ByteWriter w, int[] ids)
        {
            if (ids.Length > MaxUnitsPerCommand) throw new ArgumentException("too many units in one command");
            w.WriteU16((ushort)ids.Length);
            foreach (int id in ids) w.WriteI32(id);
        }

        // ---------------------------------------------------------------- reading

        private static bool TryRead(ByteReader r, out Command command)
        {
            command = null;
            if (!r.TryReadByte(out byte type)) return false;
            switch (type)
            {
                case TypeMove:
                    {
                        if (!TryReadIds(r, out int[] ids) || !TryReadVec(r, out Vec2 target)) return false;
                        command = new MoveCommand(ids, target);
                        return true;
                    }
                case TypeBuild:
                    {
                        if (!r.TryReadByte(out byte kind) || !IsDefined<BuildingKind>(kind)) return false;
                        if (!r.TryReadI32(out int x) || !r.TryReadI32(out int y) || !InRange(x) || !InRange(y)) return false;
                        command = new BuildCommand((BuildingKind)kind, x, y);
                        return true;
                    }
                case TypeQueue:
                    {
                        if (!r.TryReadI32(out int id) || !r.TryReadByte(out byte kind) || !IsDefined<UnitKind>(kind)) return false;
                        command = new QueueUnitCommand(id, (UnitKind)kind);
                        return true;
                    }
                case TypeCancel:
                    {
                        if (!r.TryReadI32(out int id) || !r.TryReadI32(out int index)) return false;
                        command = new CancelQueuedCommand(id, index);
                        return true;
                    }
                case TypeRally:
                    {
                        if (!r.TryReadI32(out int id) || !TryReadVec(r, out Vec2 point)) return false;
                        command = new SetRallyCommand(id, point);
                        return true;
                    }
                case TypeAttack:
                    {
                        if (!TryReadIds(r, out int[] ids)) return false;
                        if (!r.TryReadByte(out byte kind) || !IsDefined<AttackKind>(kind) || (AttackKind)kind == AttackKind.None) return false;
                        if (!r.TryReadI32(out int target)) return false;
                        command = new AttackCommand(ids, (AttackKind)kind, target);
                        return true;
                    }
                case TypeCancelBuild:
                    {
                        if (!r.TryReadI32(out int id)) return false;
                        command = new CancelBuildCommand(id);
                        return true;
                    }
                case TypeUpgradeBase:
                    command = new UpgradeBaseCommand();
                    return true;
                case TypeResearch:
                    {
                        if (!r.TryReadByte(out byte kind) || !IsDefined<UpgradeKind>(kind)) return false;
                        command = new ResearchCommand((UpgradeKind)kind);
                        return true;
                    }
                case TypeSurrender:
                    command = new SurrenderCommand();
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadIds(ByteReader r, out int[] ids)
        {
            ids = null;
            if (!r.TryReadU16(out ushort count) || count > MaxUnitsPerCommand || count * 4 > r.Remaining) return false;
            ids = new int[count];
            for (int i = 0; i < count; i++)
                if (!r.TryReadI32(out ids[i])) return false;
            return true;
        }

        private static bool TryReadVec(ByteReader r, out Vec2 v)
        {
            v = default;
            if (!r.TryReadI64(out long x) || !r.TryReadI64(out long y)) return false;
            Fix64 fx = Fix64.FromRaw(x), fy = Fix64.FromRaw(y);
            Fix64 limit = Fix64.FromInt(MaxCoordinate);
            if (fx < -limit || fx > limit || fy < -limit || fy > limit) return false;
            v = new Vec2(fx, fy);
            return true;
        }

        private static bool InRange(int v) => v >= -MaxCoordinate && v <= MaxCoordinate;

        private static bool IsDefined<T>(byte value) where T : struct => Enum.IsDefined(typeof(T), (int)value);
    }

    /// <summary>Growable byte buffer with fixed little-endian writes.</summary>
    public sealed class ByteWriter
    {
        private byte[] _buffer = new byte[64];
        private int _length;

        public int Length => _length;
        public byte[] ToArray() { var copy = new byte[_length]; Array.Copy(_buffer, copy, _length); return copy; }

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length) return;
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
        }

        public void WriteByte(byte v) { Ensure(1); _buffer[_length++] = v; }
        public void WriteU16(ushort v) { Ensure(2); _buffer[_length++] = (byte)v; _buffer[_length++] = (byte)(v >> 8); }
        public void WriteI32(int v) { Ensure(4); for (int i = 0; i < 4; i++) _buffer[_length++] = (byte)(v >> (8 * i)); }
        public void WriteI64(long v) { Ensure(8); for (int i = 0; i < 8; i++) _buffer[_length++] = (byte)(v >> (8 * i)); }
        public void WriteVec(Vec2 v) { WriteI64(v.X.Raw); WriteI64(v.Y.Raw); }
        public void WriteBytes(byte[] data) { Ensure(data.Length); Array.Copy(data, 0, _buffer, _length, data.Length); _length += data.Length; }
    }

    /// <summary>Bounds-checked reader: every read reports failure instead of throwing.</summary>
    public sealed class ByteReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _pos;

        public ByteReader(byte[] data, int start, int end) { _data = data; _pos = start; _end = end; }

        public int Position => _pos;
        public int Remaining => _end - _pos;
        public void Skip(int count) { _pos += count; }

        public bool TryReadByte(out byte v)
        {
            v = 0;
            if (Remaining < 1) return false;
            v = _data[_pos++];
            return true;
        }

        public bool TryReadU16(out ushort v)
        {
            v = 0;
            if (Remaining < 2) return false;
            v = (ushort)(_data[_pos] | (_data[_pos + 1] << 8));
            _pos += 2;
            return true;
        }

        public bool TryReadI32(out int v)
        {
            v = 0;
            if (Remaining < 4) return false;
            uint u = 0;
            for (int i = 0; i < 4; i++) u |= (uint)_data[_pos + i] << (8 * i);
            _pos += 4;
            v = (int)u;
            return true;
        }

        public bool TryReadI64(out long v)
        {
            v = 0;
            if (Remaining < 8) return false;
            ulong u = 0;
            for (int i = 0; i < 8; i++) u |= (ulong)_data[_pos + i] << (8 * i);
            _pos += 8;
            v = (long)u;
            return true;
        }
    }
}
