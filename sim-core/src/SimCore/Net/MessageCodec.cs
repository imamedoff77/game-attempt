using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// Bytes for <see cref="NetMessage"/>s. Same rules as <see cref="CommandCodec"/>: fixed layout, little-endian,
    /// reading never throws and refuses anything cut off, oversized or unknown.
    /// </summary>
    public static class MessageCodec
    {
        /// <summary>Biggest possible encoded message (a bundle with a full turn from both players).</summary>
        public const int MaxMessageBytes = 1 + 4 + 2 * (4 + 2 + CommandCodec.MaxCommandsPerTurn * (2 + CommandCodec.MaxCommandBytes));

        private const byte TypeSubmit = 1, TypeBundle = 2, TypeHash = 3, TypeDesync = 4,
            TypeCreateRoom = 5, TypeJoinRoom = 6, TypeRoomCreated = 7, TypeMatchStart = 8, TypeReject = 9, TypePeerLeft = 10, TypePing = 11,
            TypeRejoin = 12, TypeMatchResume = 13, TypePeerBack = 14,
            TypeQuickMatch = 15, TypeQueued = 16, TypeLeaveQueue = 17, TypeMatchInfo = 18, TypeReportResult = 19, TypeRatingUpdate = 20, TypeChat = 21, TypeChatFrom = 22;

        public const int MaxCodeLength = 8;

        /// <summary>Raised whenever the wire format changes; the server refuses clients with another number.</summary>
        public const int ProtocolVersion = 5;
        public const int MaxAuthTokenLength = 128;

        public static byte[] Encode(NetMessage message)
        {
            var w = new ByteWriter();
            switch (message)
            {
                case TurnSubmit s:
                    w.WriteByte(TypeSubmit);
                    w.WriteI32(s.Turn);
                    WriteBlock(w, CommandCodec.EncodeList(s.Commands));
                    break;
                case TurnBundle b:
                    if (b.ByTeam == null || b.ByTeam.Length != World.TeamCount) throw new ArgumentException("a bundle needs one command list per team");
                    w.WriteByte(TypeBundle);
                    w.WriteI32(b.Turn);
                    foreach (IReadOnlyList<Command> list in b.ByTeam) WriteBlock(w, CommandCodec.EncodeList(list));
                    break;
                case HashReport h:
                    w.WriteByte(TypeHash);
                    w.WriteI32(h.Turn);
                    w.WriteI64((long)h.Hash);
                    break;
                case DesyncNotice d:
                    w.WriteByte(TypeDesync);
                    w.WriteI32(d.Turn);
                    w.WriteI64((long)d.Hash0);
                    w.WriteI64((long)d.Hash1);
                    break;
                case CreateRoom c:
                    w.WriteByte(TypeCreateRoom);
                    w.WriteU16((ushort)c.ProtocolVersion);
                    w.WriteI64((long)c.DataHash);
                    WriteKit(w, c.Kit);
                    WriteAuth(w, c.AuthToken);
                    break;
                case JoinRoom j:
                    w.WriteByte(TypeJoinRoom);
                    w.WriteU16((ushort)j.ProtocolVersion);
                    w.WriteI64((long)j.DataHash);
                    WriteCode(w, j.Code);
                    WriteKit(w, j.Kit);
                    WriteAuth(w, j.AuthToken);
                    break;
                case RoomCreated rc:
                    w.WriteByte(TypeRoomCreated);
                    WriteCode(w, rc.Code);
                    break;
                case MatchStart ms:
                    w.WriteByte(TypeMatchStart);
                    w.WriteByte((byte)ms.Team);
                    w.WriteByte((byte)ms.InputDelayTurns);
                    w.WriteI32(ms.Seed);
                    w.WriteI64((long)ms.Token);
                    foreach (byte[] kit in ms.Kits) WriteKit(w, kit);
                    break;
                case Reject rj:
                    w.WriteByte(TypeReject);
                    w.WriteByte((byte)rj.Reason);
                    break;
                case PeerLeft pl:
                    w.WriteByte(TypePeerLeft);
                    w.WriteByte((byte)pl.Team);
                    break;
                case Ping _:
                    w.WriteByte(TypePing);
                    break;
                case Rejoin rj2:
                    w.WriteByte(TypeRejoin);
                    w.WriteU16((ushort)rj2.ProtocolVersion);
                    WriteCode(w, rj2.Code);
                    w.WriteByte((byte)rj2.Team);
                    w.WriteI64((long)rj2.Token);
                    break;
                case MatchResume mr:
                    w.WriteByte(TypeMatchResume);
                    w.WriteByte((byte)mr.Team);
                    w.WriteByte((byte)mr.InputDelayTurns);
                    w.WriteI32(mr.Seed);
                    w.WriteI32(mr.ReplayTurns);
                    w.WriteI32(mr.NextSubmitTurn);
                    foreach (byte[] kit in mr.Kits) WriteKit(w, kit);
                    break;
                case PeerBack pb:
                    w.WriteByte(TypePeerBack);
                    w.WriteByte((byte)pb.Team);
                    break;
                case QuickMatch q:
                    w.WriteByte(TypeQuickMatch);
                    w.WriteU16((ushort)q.ProtocolVersion);
                    w.WriteI64((long)q.DataHash);
                    WriteKit(w, q.Kit);
                    WriteAuth(w, q.AuthToken);
                    break;
                case Queued qd:
                    w.WriteByte(TypeQueued);
                    w.WriteI32(qd.Rating);
                    break;
                case LeaveQueue _:
                    w.WriteByte(TypeLeaveQueue);
                    break;
                case MatchInfo mi:
                    w.WriteByte(TypeMatchInfo);
                    for (int t = 0; t < World.TeamCount; t++) w.WriteI32(t < mi.Ratings.Length ? mi.Ratings[t] : 0);
                    break;
                case ReportResult rr:
                    w.WriteByte(TypeReportResult);
                    w.WriteByte((byte)rr.WinnerTeam);
                    break;
                case RatingUpdate ru:
                    w.WriteByte(TypeRatingUpdate);
                    w.WriteI32(ru.OldRating);
                    w.WriteI32(ru.NewRating);
                    break;
                case Chat ch:
                    w.WriteByte(TypeChat);
                    w.WriteByte((byte)ch.Preset);
                    break;
                case ChatFrom cf:
                    w.WriteByte(TypeChatFrom);
                    w.WriteByte((byte)cf.Team);
                    w.WriteByte((byte)cf.Preset);
                    break;
                default:
                    throw new ArgumentException("unknown message type " + (message == null ? "null" : message.GetType().Name));
            }
            return w.ToArray();
        }

        public static bool TryDecode(byte[] data, out NetMessage message) =>
            data != null ? TryDecode(data, 0, data.Length, out message) : Fail(out message);

        /// <summary>Reads one message that fills exactly data[offset .. offset+length).</summary>
        public static bool TryDecode(byte[] data, int offset, int length, out NetMessage message)
        {
            message = null;
            if (data == null || offset < 0 || length < 1 || length > MaxMessageBytes || offset > data.Length - length) return false;
            var r = new ByteReader(data, offset, offset + length);
            if (!r.TryReadByte(out byte type)) return false;

            switch (type)
            {
                case TypeSubmit:
                    {
                        if (!r.TryReadI32(out int turn) || turn < 0) return false;
                        if (!TryReadCommands(r, data, out List<Command> list) || r.Remaining != 0) return false;
                        message = new TurnSubmit(turn, list);
                        return true;
                    }
                case TypeBundle:
                    {
                        if (!r.TryReadI32(out int turn) || turn < 0) return false;
                        var byTeam = new IReadOnlyList<Command>[World.TeamCount];
                        for (int t = 0; t < byTeam.Length; t++)
                        {
                            if (!TryReadCommands(r, data, out List<Command> list)) return false;
                            byTeam[t] = list;
                        }
                        if (r.Remaining != 0) return false;
                        message = new TurnBundle(turn, byTeam);
                        return true;
                    }
                case TypeHash:
                    {
                        if (!r.TryReadI32(out int turn) || turn < 0 || !r.TryReadI64(out long hash) || r.Remaining != 0) return false;
                        message = new HashReport(turn, (ulong)hash);
                        return true;
                    }
                case TypeDesync:
                    {
                        if (!r.TryReadI32(out int turn) || turn < 0 || !r.TryReadI64(out long h0) || !r.TryReadI64(out long h1) || r.Remaining != 0) return false;
                        message = new DesyncNotice(turn, (ulong)h0, (ulong)h1);
                        return true;
                    }
                case TypeCreateRoom:
                    {
                        if (!r.TryReadU16(out ushort version) || !r.TryReadI64(out long hash)) return false;
                        if (!TryReadKit(r, out byte[] kit) || !TryReadAuth(r, out string auth) || r.Remaining != 0) return false;
                        message = new CreateRoom(version, (ulong)hash, kit, auth);
                        return true;
                    }
                case TypeJoinRoom:
                    {
                        if (!r.TryReadU16(out ushort version) || !r.TryReadI64(out long hash)) return false;
                        if (!TryReadCode(r, out string code) || !TryReadKit(r, out byte[] kit) || !TryReadAuth(r, out string auth) || r.Remaining != 0) return false;
                        message = new JoinRoom(version, (ulong)hash, code, kit, auth);
                        return true;
                    }
                case TypeRoomCreated:
                    {
                        if (!TryReadCode(r, out string code) || r.Remaining != 0) return false;
                        message = new RoomCreated(code);
                        return true;
                    }
                case TypeMatchStart:
                    {
                        if (!r.TryReadByte(out byte team) || team >= World.TeamCount) return false;
                        if (!r.TryReadByte(out byte delay) || delay < 1 || !r.TryReadI32(out int seed) || !r.TryReadI64(out long token)) return false;
                        if (!TryReadKits(r, out byte[][] kits) || r.Remaining != 0) return false;
                        message = new MatchStart(team, delay, seed, (ulong)token, kits);
                        return true;
                    }
                case TypeRejoin:
                    {
                        if (!r.TryReadU16(out ushort version) || !TryReadCode(r, out string code)) return false;
                        if (!r.TryReadByte(out byte team) || team >= World.TeamCount || !r.TryReadI64(out long token) || r.Remaining != 0) return false;
                        message = new Rejoin(version, code, team, (ulong)token);
                        return true;
                    }
                case TypeMatchResume:
                    {
                        if (!r.TryReadByte(out byte team) || team >= World.TeamCount) return false;
                        if (!r.TryReadByte(out byte delay) || delay < 1 || !r.TryReadI32(out int seed)) return false;
                        if (!r.TryReadI32(out int replay) || replay < 0 || !r.TryReadI32(out int next) || next < 0) return false;
                        if (!TryReadKits(r, out byte[][] kits) || r.Remaining != 0) return false;
                        message = new MatchResume(team, delay, seed, replay, next, kits);
                        return true;
                    }
                case TypePeerBack:
                    {
                        if (!r.TryReadByte(out byte team) || team >= World.TeamCount || r.Remaining != 0) return false;
                        message = new PeerBack(team);
                        return true;
                    }
                case TypeReject:
                    {
                        if (!r.TryReadByte(out byte reason) || !Enum.IsDefined(typeof(RejectReason), (byte)reason) || r.Remaining != 0) return false;
                        message = new Reject((RejectReason)reason);
                        return true;
                    }
                case TypePeerLeft:
                    {
                        if (!r.TryReadByte(out byte team) || team >= World.TeamCount || r.Remaining != 0) return false;
                        message = new PeerLeft(team);
                        return true;
                    }
                case TypePing:
                    if (r.Remaining != 0) return false;
                    message = new Ping();
                    return true;
                case TypeQuickMatch:
                    {
                        if (!r.TryReadU16(out ushort version) || !r.TryReadI64(out long hash)) return false;
                        if (!TryReadKit(r, out byte[] kit) || !TryReadAuth(r, out string auth) || r.Remaining != 0) return false;
                        message = new QuickMatch(version, (ulong)hash, kit, auth);
                        return true;
                    }
                case TypeQueued:
                    {
                        if (!r.TryReadI32(out int rating) || r.Remaining != 0) return false;
                        message = new Queued(rating);
                        return true;
                    }
                case TypeLeaveQueue:
                    if (r.Remaining != 0) return false;
                    message = new LeaveQueue();
                    return true;
                case TypeMatchInfo:
                    {
                        var ratings = new int[World.TeamCount];
                        for (int t = 0; t < ratings.Length; t++) if (!r.TryReadI32(out ratings[t])) return false;
                        if (r.Remaining != 0) return false;
                        message = new MatchInfo(ratings);
                        return true;
                    }
                case TypeReportResult:
                    {
                        if (!r.TryReadByte(out byte winner) || winner >= World.TeamCount || r.Remaining != 0) return false;
                        message = new ReportResult(winner);
                        return true;
                    }
                case TypeChat:
                    {
                        if (!r.TryReadByte(out byte preset) || preset >= ChatPresets.Count || r.Remaining != 0) return false;
                        message = new Chat(preset);
                        return true;
                    }
                case TypeChatFrom:
                    {
                        if (!r.TryReadByte(out byte team) || team >= World.TeamCount || !r.TryReadByte(out byte preset) || preset >= ChatPresets.Count || r.Remaining != 0) return false;
                        message = new ChatFrom(team, preset);
                        return true;
                    }
                case TypeRatingUpdate:
                    {
                        if (!r.TryReadI32(out int oldRating) || !r.TryReadI32(out int newRating) || r.Remaining != 0) return false;
                        message = new RatingUpdate(oldRating, newRating);
                        return true;
                    }
                default:
                    return false;
            }
        }

        /// <summary>Room codes are 1 to 8 characters: capital letters and digits only.</summary>
        public static bool IsValidCode(string code)
        {
            if (code == null || code.Length < 1 || code.Length > MaxCodeLength) return false;
            foreach (char c in code)
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            return true;
        }

        private static void WriteAuth(ByteWriter w, string token)
        {
            token = token ?? "";
            if (token.Length > MaxAuthTokenLength) throw new ArgumentException("auth token too long");
            w.WriteByte((byte)token.Length);
            foreach (char c in token) w.WriteByte((byte)(c >= 33 && c <= 126 ? c : '?'));
        }

        /// <summary>Printable ASCII only, so garbage cannot sneak into logs or requests.</summary>
        private static bool TryReadAuth(ByteReader r, out string token)
        {
            token = null;
            if (!r.TryReadByte(out byte length) || length > MaxAuthTokenLength || r.Remaining < length) return false;
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                r.TryReadByte(out byte b);
                if (b < 33 || b > 126) return false;
                chars[i] = (char)b;
            }
            token = new string(chars);
            return true;
        }

        private static void WriteKit(ByteWriter w, byte[] kit)
        {
            byte[] clean = NetKits.Clean(kit);
            w.WriteByte((byte)clean.Length);
            w.WriteBytes(clean);
        }

        private static bool TryReadKit(ByteReader r, out byte[] kit)
        {
            kit = null;
            if (!r.TryReadByte(out byte length) || length > NetKits.MaxKitBytes || r.Remaining < length) return false;
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                r.TryReadByte(out bytes[i]);
                if (bytes[i] > Armory.MaxLevel) return false;
            }
            kit = bytes;
            return true;
        }

        private static bool TryReadKits(ByteReader r, out byte[][] kits)
        {
            kits = new byte[World.TeamCount][];
            for (int t = 0; t < kits.Length; t++)
                if (!TryReadKit(r, out kits[t])) return false;
            return true;
        }

        private static void WriteCode(ByteWriter w, string code)
        {
            if (!IsValidCode(code)) throw new ArgumentException("bad room code");
            w.WriteByte((byte)code.Length);
            foreach (char c in code) w.WriteByte((byte)c);
        }

        private static bool TryReadCode(ByteReader r, out string code)
        {
            code = null;
            if (!r.TryReadByte(out byte length) || length < 1 || length > MaxCodeLength || r.Remaining < length) return false;
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                r.TryReadByte(out byte b);
                chars[i] = (char)b;
            }
            string text = new string(chars);
            if (!IsValidCode(text)) return false;
            code = text;
            return true;
        }

        private static bool Fail(out NetMessage message) { message = null; return false; }

        private static void WriteBlock(ByteWriter w, byte[] block)
        {
            w.WriteI32(block.Length);
            w.WriteBytes(block);
        }

        private static bool TryReadCommands(ByteReader r, byte[] data, out List<Command> commands)
        {
            commands = null;
            if (!r.TryReadI32(out int size) || size < 0 || size > r.Remaining) return false;
            if (!CommandCodec.TryDecodeList(data, r.Position, size, out commands)) return false;
            r.Skip(size);
            return true;
        }
    }
}
