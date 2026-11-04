using SimCore;

namespace SimCore.Tests;

public class CommandCodecTests
{
    private static Vec2 V(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

    private static Command[] Samples() => new Command[]
    {
        new MoveCommand(new[] { 1, 2, 3 }, new Vec2(Fix64.FromFraction(25, 2), Fix64.FromFraction(-7, 4))),
        new MoveCommand(new int[0], V(0, 0)),
        new BuildCommand(BuildingKind.VehicleFactory, 12, 34),
        new QueueUnitCommand(7, UnitKind.Tank),
        new CancelQueuedCommand(7, 2),
        new SetRallyCommand(9, V(30, 31)),
        new AttackCommand(new[] { 5, 6 }, AttackKind.Building, 11),
        new AttackCommand(new[] { 5 }, AttackKind.Unit, 12),
    };

    /// <summary>Same type and same content (commands have no Equals, so compare by re-encoding).</summary>
    private static void AssertSame(Command a, Command b)
    {
        Assert.Equal(a.GetType(), b.GetType());
        Assert.Equal(CommandCodec.Encode(a), CommandCodec.Encode(b));
    }

    [Fact]
    public void EveryCommand_SurvivesAWriteAndRead()
    {
        foreach (Command c in Samples())
        {
            byte[] bytes = CommandCodec.Encode(c);
            Assert.True(CommandCodec.TryDecode(bytes, out Command back), c.GetType().Name);
            AssertSame(c, back);
        }
    }

    [Fact]
    public void Fields_AreRestoredExactly()
    {
        var move = new MoveCommand(new[] { 4, 5 }, new Vec2(Fix64.FromFraction(1, 3), Fix64.FromRaw(123456789)));
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(move), out Command c));
        var back = (MoveCommand)c;
        Assert.Equal(new[] { 4, 5 }, back.UnitIds);
        Assert.Equal(move.Target.X.Raw, back.Target.X.Raw);
        Assert.Equal(move.Target.Y.Raw, back.Target.Y.Raw);

        var attack = new AttackCommand(new[] { 1 }, AttackKind.Building, -3);
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(attack), out Command c2));
        var back2 = (AttackCommand)c2;
        Assert.Equal(AttackKind.Building, back2.TargetKind);
        Assert.Equal(-3, back2.TargetId);
    }

    [Fact]
    public void TheLayoutIsFixed_LittleEndian()
    {
        // type 2 = build, kind 3 = Barracks, x = 1, y = 258 (0x0102)
        byte[] expected = { 2, 3, 1, 0, 0, 0, 2, 1, 0, 0 };
        Assert.Equal(expected, CommandCodec.Encode(new BuildCommand(BuildingKind.Barracks, 1, 258)));
        // type 3 = queue, building 1, unit kind 3 = Tank
        Assert.Equal(new byte[] { 3, 1, 0, 0, 0, 3 }, CommandCodec.Encode(new QueueUnitCommand(1, UnitKind.Tank)));
    }

    [Fact]
    public void TeamIsNotSent_TheReceiverStampsIt()
    {
        var c = new QueueUnitCommand(1, UnitKind.Tank) { Team = 1 };
        Assert.Equal(CommandCodec.Encode(c), CommandCodec.Encode(new QueueUnitCommand(1, UnitKind.Tank)));
        Assert.True(CommandCodec.TryDecode(CommandCodec.Encode(c), out Command back));
        Assert.Equal(0, back.Team);
    }

    [Fact]
    public void EveryCutOffPrefix_IsRefused()
    {
        foreach (Command c in Samples())
        {
            byte[] bytes = CommandCodec.Encode(c);
            for (int len = 0; len < bytes.Length; len++)
            {
                byte[] cut = bytes.Take(len).ToArray();
                Assert.False(CommandCodec.TryDecode(cut, out _), $"{c.GetType().Name} cut to {len}");
            }
        }
    }

    [Fact]
    public void ExtraBytesAtTheEnd_AreRefused()
    {
        byte[] bytes = CommandCodec.Encode(new CancelQueuedCommand(1, 2)).Concat(new byte[] { 0 }).ToArray();
        Assert.False(CommandCodec.TryDecode(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(255)]
    public void UnknownCommandType_IsRefused(byte type)
    {
        Assert.False(CommandCodec.TryDecode(new byte[] { type, 0, 0, 0, 0, 0, 0 }, out _));
    }

    [Fact]
    public void OutOfRangeValues_AreRefused()
    {
        // unknown building kind
        Assert.False(CommandCodec.TryDecode(new byte[] { 2, 99, 0, 0, 0, 0, 0, 0, 0, 0 }, out _));
        // unknown unit kind
        Assert.False(CommandCodec.TryDecode(new byte[] { 3, 1, 0, 0, 0, 99 }, out _));
        // attack with target kind None
        Assert.False(CommandCodec.TryDecode(new byte[] { 6, 0, 0, 0, 0, 0, 1, 0, 0, 0 }, out _));
        // build far outside the map
        Assert.False(CommandCodec.TryDecode(CommandCodec.Encode(new BuildCommand(BuildingKind.Barracks, 1_000_000, 0)), out _));
        // a move target with a huge coordinate
        Assert.False(CommandCodec.TryDecode(CommandCodec.Encode(new MoveCommand(new[] { 1 }, new Vec2(Fix64.FromRaw(long.MaxValue), Fix64.Zero))), out _));
    }

    [Fact]
    public void TooManyUnits_IsRefused_AndRefusedToWrite()
    {
        // count says 60000 units but there are no bytes for them
        Assert.False(CommandCodec.TryDecode(new byte[] { 1, 0x60, 0xEA, 0, 0 }, out _));
        var tooMany = new MoveCommand(Enumerable.Range(1, CommandCodec.MaxUnitsPerCommand + 1).ToArray(), V(1, 1));
        Assert.Throws<ArgumentException>(() => CommandCodec.Encode(tooMany));

        var max = new MoveCommand(Enumerable.Range(1, CommandCodec.MaxUnitsPerCommand).ToArray(), V(1, 1));
        byte[] bytes = CommandCodec.Encode(max);
        Assert.True(bytes.Length <= CommandCodec.MaxCommandBytes);
        Assert.True(CommandCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void RandomGarbage_NeverThrows()
    {
        var rng = new Random(12345);
        int accepted = 0;
        for (int i = 0; i < 50_000; i++)
        {
            var buf = new byte[rng.Next(0, 40)];
            rng.NextBytes(buf);
            if (buf.Length > 0 && rng.Next(3) > 0) buf[0] = (byte)rng.Next(1, 7); // often a real command type
            if (CommandCodec.TryDecode(buf, out Command c))
            {
                accepted++;
                // whatever was accepted must encode back to exactly the same bytes
                Assert.Equal(buf, CommandCodec.Encode(c));
            }
            Assert.False(CommandCodec.TryDecodeList(buf, 0, buf.Length, out _) && buf.Length < 2);
        }
        Assert.True(accepted > 0); // the fuzz really reaches the accepting paths
    }

    [Fact]
    public void ACommandList_RoundTrips_AndKeepsItsOrder()
    {
        Command[] list = Samples();
        byte[] bytes = CommandCodec.EncodeList(list);
        Assert.True(CommandCodec.TryDecodeList(bytes, 0, bytes.Length, out List<Command> back));
        Assert.Equal(list.Length, back.Count);
        for (int i = 0; i < list.Length; i++) AssertSame(list[i], back[i]);
    }

    [Fact]
    public void EmptyList_IsTwoBytes_AndValid()
    {
        byte[] bytes = CommandCodec.EncodeList(new List<Command>());
        Assert.Equal(new byte[] { 0, 0 }, bytes);
        Assert.True(CommandCodec.TryDecodeList(bytes, 0, bytes.Length, out List<Command> back));
        Assert.Empty(back);
    }

    [Fact]
    public void ACommandList_InsideALargerBuffer_IsReadAtItsOffset()
    {
        byte[] list = CommandCodec.EncodeList(Samples());
        byte[] padded = new byte[3].Concat(list).Concat(new byte[5]).ToArray();
        Assert.True(CommandCodec.TryDecodeList(padded, 3, list.Length, out List<Command> back));
        Assert.Equal(Samples().Length, back.Count);
        Assert.False(CommandCodec.TryDecodeList(padded, 3, list.Length + 1, out _)); // one stray byte
        Assert.False(CommandCodec.TryDecodeList(padded, 3, padded.Length, out _));    // offset past the end
    }

    [Fact]
    public void BrokenList_IsRefused()
    {
        byte[] bytes = CommandCodec.EncodeList(Samples());
        for (int len = 0; len < bytes.Length; len++)
            Assert.False(CommandCodec.TryDecodeList(bytes, 0, len, out _), "cut to " + len);

        // more commands than a turn may hold
        Assert.False(CommandCodec.TryDecodeList(new byte[] { 0xFF, 0x00 }, 0, 2, out _));
        Assert.Throws<ArgumentException>(() => CommandCodec.EncodeList(
            Enumerable.Range(0, CommandCodec.MaxCommandsPerTurn + 1).Select(_ => (Command)new CancelQueuedCommand(1, 0)).ToList()));
        // negative / overflowing ranges
        Assert.False(CommandCodec.TryDecodeList(bytes, -1, 2, out _));
        Assert.False(CommandCodec.TryDecodeList(bytes, 0, -1, out _));
        Assert.False(CommandCodec.TryDecodeList(bytes, int.MaxValue, 5, out _));
        Assert.False(CommandCodec.TryDecodeList(null!, 0, 0, out _));
    }

    [Fact]
    public void DecodedCommands_DriveTheWorldTheSameAsTheOriginals()
    {
        // The wire must not change meaning: play the same game with original and re-decoded commands.
        ulong Play(bool throughTheWire)
        {
            var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
            w.PlaceBuilding(BuildingKind.Hq, 8, 8, 0);
            Building rx = w.PlaceBuilding(BuildingKind.Barracks, 8, 16, 0);
            w.PlaceBuilding(BuildingKind.PowerPlant, 16, 8, 0);
            var cmds = new List<Command>
            {
                new QueueUnitCommand(rx.Id, UnitKind.Rifleman),
                new BuildCommand(BuildingKind.SupplyCenter, 30, 30),
                new SetRallyCommand(rx.Id, V(20, 30)),
            };
            if (throughTheWire)
            {
                byte[] bytes = CommandCodec.EncodeList(cmds);
                Assert.True(CommandCodec.TryDecodeList(bytes, 0, bytes.Length, out cmds!));
            }
            foreach (Command c in cmds) w.Enqueue(c, 0);
            for (int i = 0; i < 300; i++) w.Step();
            return w.StateHash();
        }
        Assert.Equal(Play(false), Play(true));
    }
}
