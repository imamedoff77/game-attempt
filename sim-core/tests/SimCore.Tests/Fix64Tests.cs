using System.Numerics;
using SimCore;

namespace SimCore.Tests;

public class Fix64Tests
{
    [Fact]
    public void FromInt_RoundTrips()
    {
        Assert.Equal(7, Fix64.FromInt(7).ToInt());
        Assert.Equal(-3, Fix64.FromInt(-3).ToInt());
        Assert.Equal(1L << 32, Fix64.One.Raw);
    }

    [Fact]
    public void Addition_And_Subtraction_AreExact()
    {
        Fix64 a = Fix64.FromFraction(1, 4);
        Fix64 b = Fix64.FromFraction(1, 2);
        Assert.Equal(Fix64.FromFraction(3, 4), a + b);
        Assert.Equal(Fix64.FromFraction(-1, 4), a - b);
    }

    [Theory]
    [InlineData(3, 4, 12)]
    [InlineData(-3, 4, -12)]
    [InlineData(-3, -4, 12)]
    [InlineData(0, 99, 0)]
    public void Multiplication_Integers(int a, int b, int expected)
    {
        Assert.Equal(Fix64.FromInt(expected), Fix64.FromInt(a) * Fix64.FromInt(b));
    }

    [Fact]
    public void Multiplication_Fractions()
    {
        Assert.Equal(Fix64.FromFraction(1, 4), Fix64.Half * Fix64.Half);
        Assert.Equal(Fix64.FromFraction(-3, 8), Fix64.FromFraction(3, 4) * Fix64.FromFraction(-1, 2));
    }

    [Fact]
    public void Multiplication_MatchesBigIntegerReference_ForManyRandomValues()
    {
        var rng = new DeterministicRandom(42);
        for (int i = 0; i < 20000; i++)
        {
            // Keep operands within +-2^15 so the product stays inside the +-2^31 range.
            long ra = (long)rng.NextUInt32() * 32768 - (1L << 46);
            long rb = (long)rng.NextUInt32() * 32768 - (1L << 46);
            Fix64 a = Fix64.FromRaw(ra), b = Fix64.FromRaw(rb);

            BigInteger product = BigInteger.Abs((BigInteger)ra * rb) >> 32; // magnitude, floored
            long magnitude = (long)product;
            long expected = ((ra < 0) ^ (rb < 0)) ? -magnitude : magnitude;

            Assert.Equal(expected, (a * b).Raw);
        }
    }

    [Fact]
    public void Division_Basics()
    {
        Assert.Equal(Fix64.FromInt(3), Fix64.FromInt(12) / Fix64.FromInt(4));
        Assert.Equal(Fix64.Half, Fix64.One / Fix64.FromInt(2));
        Assert.Equal(Fix64.FromFraction(-1, 4), Fix64.FromInt(-1) / Fix64.FromInt(4));
        Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(144)]
    [InlineData(10000)]
    public void Sqrt_OfPerfectSquares_IsExact(int root)
    {
        Fix64 square = Fix64.FromInt(root) * Fix64.FromInt(root);
        Assert.Equal(Fix64.FromInt(root), Fix64.Sqrt(square));
    }

    [Fact]
    public void Sqrt_Of2_IsCloseAndDeterministic()
    {
        Fix64 r = Fix64.Sqrt(Fix64.FromInt(2));
        Assert.InRange(r.ToFloat(), 1.41421356f - 1e-6f, 1.41421356f + 1e-6f);
        Assert.Equal(6074000999L, r.Raw); // floor(sqrt(2) * 2^32), pinned: must never change between builds/devices
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.Sqrt(Fix64.FromInt(-1)));
    }

    [Fact]
    public void Comparison_And_ClampWork()
    {
        Assert.True(Fix64.FromInt(2) > Fix64.One);
        Assert.Equal(Fix64.One, Fix64.Clamp(Fix64.FromInt(-5), Fix64.One, Fix64.FromInt(3)));
        Assert.Equal(Fix64.FromInt(3), Fix64.Clamp(Fix64.FromInt(9), Fix64.One, Fix64.FromInt(3)));
        Assert.Equal(Fix64.FromInt(4), Fix64.Abs(Fix64.FromInt(-4)));
    }
}
