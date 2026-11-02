namespace SimCore
{
    /// <summary>
    /// FNV-1a 64-bit hash over simulation state. Clients exchange this value every few ticks
    /// to detect desyncs. The order in which values are added must be identical on all clients.
    /// </summary>
    public struct StateHasher
    {
        private const ulong Prime = 0x100000001B3UL;
        private const ulong Offset = 0xCBF29CE484222325UL;

        private ulong _hash;

        public static StateHasher Create() => new StateHasher { _hash = Offset };

        public ulong Value => _hash;

        public void Add(long value)
        {
            ulong v = (ulong)value;
            for (int i = 0; i < 8; i++)
            {
                _hash ^= (v >> (i * 8)) & 0xFF;
                _hash *= Prime;
            }
        }

        public void Add(int value) => Add((long)value);
        public void Add(ulong value) => Add((long)value);
        public void Add(Fix64 value) => Add(value.Raw);
        public void Add(Vec2 value) { Add(value.X); Add(value.Y); }
    }
}
