using System;

namespace SimCore
{
    /// <summary>2D fixed-point vector (map plane; the 3D view maps it onto X/Z).</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly Fix64 X;
        public readonly Fix64 Y;

        public Vec2(Fix64 x, Fix64 y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(Fix64.Zero, Fix64.Zero);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, Fix64 s) => new Vec2(a.X * s, a.Y * s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public Fix64 Dot(Vec2 other) => X * other.X + Y * other.Y;

        /// <summary>Prefer this over Length for range checks: no square root needed.</summary>
        public Fix64 SqrLength => X * X + Y * Y;

        public Fix64 Length => Fix64.Sqrt(SqrLength);

        public static Fix64 SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;

        /// <summary>Unit vector, or Zero for the zero vector.</summary>
        public Vec2 Normalized()
        {
            Fix64 len = Length;
            return len == Fix64.Zero ? Zero : new Vec2(X / len, Y / len);
        }

        public bool Equals(Vec2 other) => this == other;
        public override bool Equals(object obj) => obj is Vec2 other && this == other;
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => "(" + X + ", " + Y + ")";
    }
}
