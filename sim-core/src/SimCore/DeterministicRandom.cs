using System;

namespace SimCore
{
    /// <summary>
    /// xorshift64* generator. Integer-only, so every client produces the same sequence from the same seed.
    /// Part of the simulation state: it must be included in the state hash and in snapshots.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            State = seed;
        }

        /// <summary>Full generator state, for snapshots and hashing.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value == 0 ? 0x9E3779B97F4A7C15UL : value; // xorshift must never hold 0
        }

        public uint NextUInt32()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return (uint)((x * 0x2545F4914F6CDD1DUL) >> 32);
        }

        /// <summary>Uniform-ish integer in [0, maxExclusive). Multiply-shift, no modulo.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(((ulong)NextUInt32() * (ulong)maxExclusive) >> 32);
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return minInclusive + Next(maxExclusive - minInclusive);
        }

        /// <summary>Fixed-point value in [0, 1).</summary>
        public Fix64 NextFix() => Fix64.FromRaw(NextUInt32());
    }
}
