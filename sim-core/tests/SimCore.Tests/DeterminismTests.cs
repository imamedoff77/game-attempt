using SimCore;

namespace SimCore.Tests;

public class DeterminismTests
{
    [Fact]
    public void Random_MatchesIndependentPythonReference()
    {
        // Reference values computed outside this codebase (Python, same xorshift64* definition).
        var rng = new DeterministicRandom(12345);
        uint[] expected = { 2555902770, 3234773579, 328846939, 3161420795, 513335584 };
        foreach (uint e in expected)
            Assert.Equal(e, rng.NextUInt32());
    }

    [Fact]
    public void Random_SameSeed_SameSequence_DifferentSeed_DiffersEventually()
    {
        var a = new DeterministicRandom(7);
        var b = new DeterministicRandom(7);
        var c = new DeterministicRandom(8);
        bool differs = false;
        for (int i = 0; i < 100; i++)
        {
            uint va = a.NextUInt32();
            Assert.Equal(va, b.NextUInt32());
            if (va != c.NextUInt32()) differs = true;
        }
        Assert.True(differs);
    }

    [Fact]
    public void Random_ZeroSeed_DoesNotStick()
    {
        var rng = new DeterministicRandom(0);
        Assert.NotEqual(rng.NextUInt32(), rng.NextUInt32());
    }

    [Fact]
    public void Random_Next_StaysInRange()
    {
        var rng = new DeterministicRandom(99);
        for (int i = 0; i < 10000; i++)
        {
            int v = rng.Next(-5, 6);
            Assert.InRange(v, -5, 5);
        }
    }

    [Fact]
    public void Random_StateCanBeSnapshotted_AndResumed()
    {
        var rng = new DeterministicRandom(5);
        rng.NextUInt32();
        ulong snapshot = rng.State;
        uint next = rng.NextUInt32();

        var resumed = new DeterministicRandom(1) { State = snapshot };
        Assert.Equal(next, resumed.NextUInt32());
    }

    [Fact]
    public void StateHasher_SameInputsSameHash_OrderMatters()
    {
        ulong Hash(params int[] values)
        {
            var h = StateHasher.Create();
            foreach (int v in values) h.Add(v);
            return h.Value;
        }

        Assert.Equal(Hash(1, 2, 3), Hash(1, 2, 3));
        Assert.NotEqual(Hash(1, 2, 3), Hash(3, 2, 1));
        Assert.NotEqual(Hash(1, 2, 3), Hash(1, 2, 4));
    }

    [Fact]
    public void Vec2_LengthAndNormalize()
    {
        var v = new Vec2(Fix64.FromInt(3), Fix64.FromInt(4));
        Assert.Equal(Fix64.FromInt(25), v.SqrLength);
        Assert.Equal(Fix64.FromInt(5), v.Length);

        Vec2 n = v.Normalized();
        Assert.InRange(n.Length.ToFloat(), 0.9999f, 1.0001f);
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
    }
}

public class HashReferenceTests
{
    [Fact]
    public void StateHasher_MatchesIndependentPythonReference()
    {
        var h = StateHasher.Create();
        h.Add(1); h.Add(2); h.Add(3);
        Assert.Equal(0xDA2BFB225E0D1F05UL, h.Value);
    }
}
