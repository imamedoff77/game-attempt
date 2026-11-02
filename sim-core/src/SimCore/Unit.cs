namespace SimCore
{
    /// <summary>A mobile unit. Only the World may change it.</summary>
    public sealed class Unit
    {
        public int Id { get; }
        public UnitKind Kind { get; }

        /// <summary>Position now, and at the start of the current tick (for smooth rendering).</summary>
        public Vec2 Pos { get; internal set; }
        public Vec2 PrevPos { get; internal set; }

        /// <summary>Final destination of the current order.</summary>
        public Vec2 Target { get; internal set; }
        public bool Moving { get; internal set; }

        /// <summary>Waypoints to walk through, and which one is next.</summary>
        public Vec2[] Path { get; internal set; }
        public int PathIndex { get; internal set; }

        /// <summary>Distance covered per simulation tick.</summary>
        public Fix64 StepPerTick { get; }

        internal Unit(int id, UnitKind kind, Vec2 pos, Fix64 stepPerTick)
        {
            Id = id;
            Kind = kind;
            Pos = pos;
            PrevPos = pos;
            Target = pos;
            StepPerTick = stepPerTick;
        }
    }
}
