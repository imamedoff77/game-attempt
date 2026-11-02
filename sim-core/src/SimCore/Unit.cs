namespace SimCore
{
    /// <summary>A mobile unit. Only the World may change it.</summary>
    public sealed class Unit
    {
        public int Id { get; }

        /// <summary>Position now, and at the start of the current tick (for smooth rendering).</summary>
        public Vec2 Pos { get; internal set; }
        public Vec2 PrevPos { get; internal set; }

        public Vec2 Target { get; internal set; }
        public bool Moving { get; internal set; }

        /// <summary>Distance covered per simulation tick.</summary>
        public Fix64 StepPerTick { get; }

        internal Unit(int id, Vec2 pos, Fix64 stepPerTick)
        {
            Id = id;
            Pos = pos;
            PrevPos = pos;
            Target = pos;
            StepPerTick = stepPerTick;
        }
    }
}
