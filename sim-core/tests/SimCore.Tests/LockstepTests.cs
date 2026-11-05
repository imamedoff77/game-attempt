using SimCore;

namespace SimCore.Tests;

public class MessageCodecTests
{
    private static Vec2 V(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

    private static NetMessage[] Samples() => new NetMessage[]
    {
        new TurnSubmit(0, new List<Command>()),
        new TurnSubmit(41, new List<Command> { new MoveCommand(new[] { 1, 2 }, V(3, 4)), new BuildCommand(BuildingKind.Barracks, 5, 6) }),
        new TurnBundle(7, new IReadOnlyList<Command>[] { new List<Command> { new QueueUnitCommand(1, UnitKind.Tank) }, new List<Command>() }),
        new HashReport(19, 0xDEADBEEFCAFEF00DUL),
        new DesyncNotice(24, 1UL, ulong.MaxValue),
        new CreateRoom(3, 0x0123456789ABCDEFUL, new byte[] { 0, 1, 5 }, "tok-1"),
        new JoinRoom(3, ulong.MaxValue, "K7Q2", new byte[] { 2, 2 }, "tok-2"),
        new RoomCreated("AB12"),
        new MatchStart(1, 2, 123456789, 0, new[] { new byte[] { 1, 2 }, new byte[] { 5 } }),
        new Reject(RejectReason.WrongData),
        new PeerLeft(0),
        new Ping(),
        new Rejoin(1, "AB12", 1, 0xFEEDFACECAFEBEEFUL),
        new MatchResume(1, 2, 99, 1500, 1503),
        new PeerBack(0),
        new QuickMatch(4, 0x0123456789ABCDEFUL, new byte[] { 1, 2 }, "tok-q"),
        new Queued(1234),
        new LeaveQueue(),
        new MatchInfo(new[] { 1000, 1210 }),
        new ReportResult(1),
        new RatingUpdate(1000, 1016),
        new Chat(3),
        new ChatFrom(1, 7),
    };

    [Fact]
    public void Kits_AreCleanedOnTheWay_AndBadOnesAreRefused()
    {
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new CreateRoom(2, 1, new byte[] { 9, 3 })), out NetMessage m));
        byte[] kit = ((CreateRoom)m).Kit;
        Assert.Equal(Armory.FromBytes(null).ToBytes().Length, kit.Length);
        Assert.Equal(Armory.MaxLevel, kit[0]);
        Assert.Equal(3, kit[1]);

        // a hand-made packet with a level above the maximum, and one with a too long kit, are not accepted
        byte[] good = MessageCodec.Encode(new CreateRoom(2, 1, new byte[0]));
        byte[] bad = (byte[])good.Clone();
        bad[bad.Length - 2] = 200; // the last kit byte (one byte of auth length follows)
        Assert.False(MessageCodec.TryDecode(bad, out _));
        byte[] tooLong = (byte[])good.Clone();
        tooLong[good.Length - 2 - kit.Length] = 40; // the kit length byte
        Assert.False(MessageCodec.TryDecode(tooLong, out _));

        var start = new MatchStart(0, 2, 1, 7, new[] { new byte[] { 1 }, null });
        Assert.Equal(World.TeamCount, start.Kits.Length);
        Assert.All(start.Kits, k => Assert.Equal(kit.Length, k.Length));
    }

    [Fact]
    public void AuthToken_RoundTrips_AndBadOnesAreRefused()
    {
        string token = "AbC-123_xyz";
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new CreateRoom(3, 1, null, token)), out NetMessage m));
        Assert.Equal(token, ((CreateRoom)m).AuthToken);
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new JoinRoom(3, 1, "K7Q2", null, token)), out m));
        Assert.Equal(token, ((JoinRoom)m).AuthToken);
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new CreateRoom(3, 1)), out m));
        Assert.Equal("", ((CreateRoom)m).AuthToken);

        byte[] good = MessageCodec.Encode(new CreateRoom(3, 1, null, "abc"));
        byte[] control = (byte[])good.Clone();
        control[control.Length - 1] = 7; // not printable
        Assert.False(MessageCodec.TryDecode(control, out _));
        byte[] lying = (byte[])good.Clone();
        lying[lying.Length - 4] = 200; // says 200 characters, has 3
        Assert.False(MessageCodec.TryDecode(lying, out _));
        Assert.Throws<System.ArgumentException>(() => MessageCodec.Encode(new CreateRoom(3, 1, null, new string('a', 129))));
    }

    [Fact]
    public void EveryMessage_SurvivesAWriteAndRead_AndEncodesIdentically()
    {
        foreach (NetMessage m in Samples())
        {
            byte[] bytes = MessageCodec.Encode(m);
            Assert.True(MessageCodec.TryDecode(bytes, out NetMessage back), m.GetType().Name);
            Assert.Equal(m.GetType(), back.GetType());
            Assert.Equal(bytes, MessageCodec.Encode(back));
        }
    }

    [Fact]
    public void LobbyMessages_KeepTheirFields()
    {
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new JoinRoom(1, 0xFFFFFFFFFFFFFFFFUL, "K7Q2")), out NetMessage m));
        var join = (JoinRoom)m;
        Assert.Equal("K7Q2", join.Code);
        Assert.Equal(ulong.MaxValue, join.DataHash);

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new MatchStart(1, 3, -5)), out m));
        var start = (MatchStart)m;
        Assert.Equal((1, 3, -5), (start.Team, start.InputDelayTurns, start.Seed));

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new Rejoin(1, "AB12", 1, 0xFEEDFACECAFEBEEFUL)), out m));
        var rejoin = (Rejoin)m;
        Assert.Equal(("AB12", 1, 0xFEEDFACECAFEBEEFUL), (rejoin.Code, rejoin.Team, rejoin.Token));

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new MatchResume(1, 2, 99, 1500, 1503)), out m));
        var resume = (MatchResume)m;
        Assert.Equal((1, 2, 99, 1500, 1503), (resume.Team, resume.InputDelayTurns, resume.Seed, resume.ReplayTurns, resume.NextSubmitTurn));
    }

    [Fact]
    public void RankedMessages_KeepTheirFields_AndBadOnesAreRefused()
    {
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new QuickMatch(4, 9, new byte[] { 3 }, "abc")), out NetMessage m));
        var quick = (QuickMatch)m;
        Assert.Equal((4, 9UL, "abc"), (quick.ProtocolVersion, quick.DataHash, quick.AuthToken));
        Assert.Equal(3, quick.Kit[0]);

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new MatchInfo(new[] { 1000, -5 })), out m));
        Assert.Equal(new[] { 1000, -5 }, ((MatchInfo)m).Ratings);
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new RatingUpdate(980, 1002)), out m));
        Assert.Equal((980, 1002), (((RatingUpdate)m).OldRating, ((RatingUpdate)m).NewRating));

        Assert.False(MessageCodec.TryDecode(new byte[] { 21, ChatPresets.Count }, out _)); // a phrase that does not exist
        Assert.False(MessageCodec.TryDecode(new byte[] { 22, 2, 0 }, out _)); // a team that does not exist
        Assert.True(MessageCodec.TryDecode(new byte[] { 21, (byte)(ChatPresets.Count - 1) }, out m));
        Assert.Equal(ChatPresets.Count - 1, ((Chat)m).Preset);
        Assert.False(MessageCodec.TryDecode(new byte[] { 19, 2 }, out _)); // a winner that is not a team
        Assert.False(MessageCodec.TryDecode(new byte[] { 19 }, out _));
        Assert.False(MessageCodec.TryDecode(new byte[] { 16, 1, 0 }, out _)); // cut off
        Assert.False(MessageCodec.TryDecode(new byte[] { 17, 0 }, out _)); // trailing byte
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]          // lower case
    [InlineData("AB CD")]
    [InlineData("TOOLONG123")]
    [InlineData("A-B")]
    public void BadRoomCodes_AreRejected(string code)
    {
        Assert.False(MessageCodec.IsValidCode(code));
        Assert.ThrowsAny<Exception>(() => MessageCodec.Encode(new RoomCreated(code)));
    }

    [Fact]
    public void LobbyMessages_WithBadValues_AreRefusedWhenDecoded()
    {
        byte[] start = MessageCodec.Encode(new MatchStart(1, 2, 7));
        start[1] = 2;                                         // team 2 does not exist
        Assert.False(MessageCodec.TryDecode(start, out _));

        byte[] reject = MessageCodec.Encode(new Reject(RejectReason.RoomFull));
        reject[1] = 99;                                       // unknown reason
        Assert.False(MessageCodec.TryDecode(reject, out _));

        byte[] ping = MessageCodec.Encode(new Ping());
        Assert.False(MessageCodec.TryDecode(new[] { ping[0], (byte)0 }, out _)); // trailing byte
    }

    [Fact]
    public void Fields_AreRestored()
    {
        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(new HashReport(19, 0xDEADBEEFCAFEF00DUL)), out NetMessage m));
        var h = (HashReport)m;
        Assert.Equal(19, h.Turn);
        Assert.Equal(0xDEADBEEFCAFEF00DUL, h.Hash);

        Assert.True(MessageCodec.TryDecode(MessageCodec.Encode(Samples()[2]), out NetMessage b));
        var bundle = (TurnBundle)b;
        Assert.Equal(7, bundle.Turn);
        Assert.Single(bundle.ByTeam[0]);
        Assert.Empty(bundle.ByTeam[1]);
    }

    [Fact]
    public void EveryCutOffPrefix_IsRefused_AndTrailingBytesToo()
    {
        foreach (NetMessage m in Samples())
        {
            byte[] bytes = MessageCodec.Encode(m);
            for (int len = 0; len < bytes.Length; len++)
                Assert.False(MessageCodec.TryDecode(bytes.Take(len).ToArray(), out _), $"{m.GetType().Name} cut to {len}");
            Assert.False(MessageCodec.TryDecode(bytes.Concat(new byte[] { 0 }).ToArray(), out _));
        }
    }

    [Fact]
    public void NegativeTurn_UnknownType_AndBundleWithWrongListCount_AreRefused()
    {
        byte[] bad = MessageCodec.Encode(new HashReport(1, 5));
        bad[1] = bad[2] = bad[3] = bad[4] = 0xFF; // turn = -1
        Assert.False(MessageCodec.TryDecode(bad, out _));
        Assert.False(MessageCodec.TryDecode(new byte[] { 9, 0, 0, 0, 0 }, out _));
        Assert.Throws<ArgumentException>(() => MessageCodec.Encode(new TurnBundle(1, new IReadOnlyList<Command>[] { new List<Command>() })));
    }

    [Fact]
    public void RandomGarbage_NeverThrows_AndAcceptedOnesEncodeBackIdentically()
    {
        var rng = new Random(777);
        int accepted = 0;
        for (int i = 0; i < 50_000; i++)
        {
            var buf = new byte[rng.Next(0, 60)];
            rng.NextBytes(buf);
            if (buf.Length > 0 && rng.Next(3) > 0) buf[0] = (byte)rng.Next(1, 5);
            if (buf.Length > 4 && rng.Next(2) == 0) { buf[2] = buf[3] = buf[4] = 0; buf[1] &= 0x7F; } // small positive turn
            if (MessageCodec.TryDecode(buf, out NetMessage m))
            {
                accepted++;
                Assert.Equal(buf, MessageCodec.Encode(m));
            }
        }
        Assert.True(accepted > 0);
    }

    [Fact]
    public void ABiggestPossibleBundle_FitsTheStatedLimit()
    {
        var full = Enumerable.Range(0, CommandCodec.MaxCommandsPerTurn)
            .Select(_ => (Command)new MoveCommand(Enumerable.Range(1, CommandCodec.MaxUnitsPerCommand).ToArray(), V(1, 1))).ToList();
        byte[] bytes = MessageCodec.Encode(new TurnBundle(1, new IReadOnlyList<Command>[] { full, full }));
        Assert.True(bytes.Length <= MessageCodec.MaxMessageBytes, $"{bytes.Length} > {MessageCodec.MaxMessageBytes}");
        Assert.True(MessageCodec.TryDecode(bytes, out _));
    }
}

public class RelayTests
{
    private static Vec2 V(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

    private static (LockstepRelay relay, List<(int to, NetMessage msg)> sent) NewRelay()
    {
        var sent = new List<(int, NetMessage)>();
        return (new LockstepRelay((to, msg) => sent.Add((to, msg))), sent);
    }

    private static TurnSubmit Submit(int turn, params Command[] cmds) => new TurnSubmit(turn, cmds.ToList());

    [Fact]
    public void ATurnIsSent_OnlyWhenBothPlayersSubmitted_ToBoth_WithTeamsAsSlots()
    {
        var (relay, sent) = NewRelay();
        var move = new MoveCommand(new[] { 1 }, V(1, 1));
        var queue = new QueueUnitCommand(2, UnitKind.Rifleman);

        relay.Receive(0, Submit(0, move));
        Assert.Empty(sent);
        relay.Receive(1, Submit(0, queue));
        Assert.Equal(2, sent.Count);
        Assert.Equal(new[] { 0, 1 }, sent.Select(s => s.to).OrderBy(x => x).ToArray());

        var bundle = (TurnBundle)sent[0].msg;
        Assert.Equal(0, bundle.Turn);
        Assert.Same(move, bundle.ByTeam[0][0]);
        Assert.Same(queue, bundle.ByTeam[1][0]);
        Assert.Equal(1, relay.TurnsBundled);
    }

    [Fact]
    public void ARunAheadPlayer_DoesNotMakeTurnsComplete_UntilTheOtherCatchesUp_InOrder()
    {
        var (relay, sent) = NewRelay();
        for (int t = 0; t < 3; t++) relay.Receive(0, Submit(t));
        Assert.Empty(sent);
        relay.Receive(1, Submit(0));
        relay.Receive(1, Submit(1));
        relay.Receive(1, Submit(2));
        Assert.Equal(6, sent.Count);
        Assert.Equal(new[] { 0, 0, 1, 1, 2, 2 }, sent.Select(s => ((TurnBundle)s.msg).Turn).ToArray());
    }

    [Fact]
    public void OutOfOrderDuplicateOrFarAheadSubmits_AreRefused()
    {
        var (relay, sent) = NewRelay();
        relay.Receive(0, Submit(1));                 // skipped turn 0
        relay.Receive(0, Submit(0));
        relay.Receive(0, Submit(0));                 // duplicate
        Assert.Equal(2, relay.Violations);

        for (int t = 1; t <= LockstepRelay.MaxTurnsAhead; t++) relay.Receive(0, Submit(t));
        Assert.Equal(2, relay.Violations);
        relay.Receive(0, Submit(LockstepRelay.MaxTurnsAhead + 1)); // too far ahead of the slowest player
        Assert.Equal(3, relay.Violations);
        Assert.Empty(sent);
    }

    [Fact]
    public void InvalidTeam_NullMessage_AndWrongMessageKinds_AreViolations()
    {
        var (relay, sent) = NewRelay();
        relay.Receive(2, Submit(0));
        relay.Receive(-1, Submit(0));
        relay.Receive(0, null!);
        relay.Receive(0, new TurnBundle(0, new IReadOnlyList<Command>[] { new List<Command>(), new List<Command>() })); // only the server sends these
        relay.Receive(0, new DesyncNotice(0, 1, 2));
        Assert.Equal(5, relay.Violations);
        Assert.Empty(sent);
    }

    [Fact]
    public void MatchingHashes_AreCounted_DifferentOnesStopTheMatch()
    {
        var (relay, sent) = NewRelay();
        relay.Receive(0, new HashReport(4, 111));
        relay.Receive(1, new HashReport(4, 111));
        Assert.Equal(1, relay.HashesCompared);
        Assert.False(relay.Desynced);

        relay.Receive(1, new HashReport(9, 222));
        relay.Receive(0, new HashReport(9, 333));
        Assert.True(relay.Desynced);
        Assert.Equal(9, relay.DesyncInfo.Turn);
        Assert.Equal(333UL, relay.DesyncInfo.Hash0);
        Assert.Equal(222UL, relay.DesyncInfo.Hash1);
        Assert.Equal(2, sent.Count(s => s.msg is DesyncNotice));

        // nothing is sent after that
        int before = sent.Count;
        relay.Receive(0, Submit(0));
        relay.Receive(1, Submit(0));
        Assert.Equal(before, sent.Count);
    }

    [Fact]
    public void TheLog_KeepsEveryTurnInOrder()
    {
        var (relay, _) = NewRelay();
        for (int t = 0; t < 10; t++) { relay.Receive(0, Submit(t)); relay.Receive(1, Submit(t)); }
        Assert.Equal(10, relay.Log.Count);
        for (int t = 0; t < 10; t++) Assert.Equal(t, relay.Log[t].Turn);
    }
}
