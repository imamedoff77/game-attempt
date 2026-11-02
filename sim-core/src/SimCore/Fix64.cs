using System;
using System.Numerics;

namespace SimCore
{
    /// <summary>
    /// Q31.32 fixed-point number backed by a long. Integer-only arithmetic, so results are
    /// bit-identical on every device. Never use float/double inside the simulation.
    /// Overflow wraps silently; the simulation must keep values inside +-2^31.
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public const int FractionBits = 32;
        private const long OneRaw = 1L << FractionBits;

        public readonly long Raw;

        private Fix64(long raw) { Raw = raw; }

        public static Fix64 FromRaw(long raw) => new Fix64(raw);
        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionBits);

        /// <summary>Exact fraction numerator/denominator, truncated toward zero.</summary>
        public static Fix64 FromFraction(int numerator, int denominator)
        {
            if (denominator == 0) throw new DivideByZeroException();
            return new Fix64((((long)numerator) << FractionBits) / denominator);
        }

        public static readonly Fix64 Zero = new Fix64(0);
        public static readonly Fix64 One = new Fix64(OneRaw);
        public static readonly Fix64 Half = new Fix64(OneRaw >> 1);
        public static readonly Fix64 MaxValue = new Fix64(long.MaxValue);
        public static readonly Fix64 MinValue = new Fix64(long.MinValue);

        public int ToInt() => (int)(Raw >> FractionBits);

        /// <summary>For rendering and debugging only. Never feed the result back into the simulation.</summary>
        public float ToFloat() => (float)Raw / OneRaw;

        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(a.Raw + b.Raw);
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(a.Raw - b.Raw);
        public static Fix64 operator -(Fix64 a) => new Fix64(-a.Raw);

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            bool negative = (a.Raw < 0) ^ (b.Raw < 0);
            ulong ua = Abs(a.Raw);
            ulong ub = Abs(b.Raw);

            ulong aHi = ua >> 32, aLo = ua & 0xFFFFFFFFUL;
            ulong bHi = ub >> 32, bLo = ub & 0xFFFFFFFFUL;

            // (ua * ub) >> 32, computed without a 128-bit type.
            ulong result = ((aHi * bHi) << 32) + (aHi * bLo) + (aLo * bHi) + ((aLo * bLo) >> 32);
            long signed = (long)result;
            return new Fix64(negative ? -signed : signed);
        }

        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();
            BigInteger numerator = (BigInteger)a.Raw << FractionBits;
            return new Fix64((long)(numerator / b.Raw)); // BigInteger division truncates toward zero.
        }

        public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
        public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
        public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
        public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

        public static implicit operator Fix64(int value) => FromInt(value);

        public static Fix64 Abs(Fix64 value) => value.Raw < 0 ? new Fix64(-value.Raw) : value;
        public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw <= b.Raw ? a : b;
        public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw >= b.Raw ? a : b;

        public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
            => value.Raw < min.Raw ? min : (value.Raw > max.Raw ? max : value);

        /// <summary>Square root, rounded down. Throws for negative input.</summary>
        public static Fix64 Sqrt(Fix64 value)
        {
            if (value.Raw < 0) throw new ArgumentOutOfRangeException(nameof(value), "Square root of a negative number.");
            if (value.Raw == 0) return Zero;

            // sqrt(raw / 2^32) * 2^32 == sqrt(raw * 2^32)
            BigInteger n = (BigInteger)value.Raw << FractionBits;
            BigInteger x = n;
            BigInteger y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }
            return new Fix64((long)x);
        }

        private static ulong Abs(long value) => value < 0 ? (ulong)(-value) : (ulong)value;

        public bool Equals(Fix64 other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix64 other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);
        public override string ToString() => ToFloat().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
