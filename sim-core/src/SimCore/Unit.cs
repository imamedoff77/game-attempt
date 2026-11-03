namespace SimCore
{
    /// <summary>A mobile unit. Only the World may change it.</summary>
    public sealed class Unit
    {
        public int Id { get; }
        public UnitKind Kind { get; }
        /// <summary>0 = player, 1 = enemy.</summary>
        public int Team { get; }

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

        public int Hp { get; internal set; }
        public int MaxHp { get; }

        /// <summary>What it is shooting at (AttackKind.None = nothing).</summary>
        public AttackKind AttackKind { get; internal set; }
        public int AttackId { get; internal set; }
        /// <summary>True when the player ordered the attack (it then chases without giving up).</summary>
        public bool Ordered { get; internal set; }
        internal int Cooldown;

        /// <summary>Speed limit (per tick) while moving as a group, so fast units do not run ahead of slow ones. 0 = none.</summary>
        internal Fix64 SpeedCap;

        /// <summary>Where the last shot went, and when (so the view can turn the unit towards it).</summary>
        public Vec2 AimPoint { get; internal set; }
        public int LastShotTick { get; internal set; } = -1000;

        /// <summary>Computer-controlled guard: notices enemies, chases a bit, walks back, optionally patrols.</summary>
        public bool Guard { get; internal set; }
        public Vec2 Home { get; internal set; }
        public Vec2[] Patrol { get; internal set; }
        public int PatrolIndex { get; internal set; }

        internal Unit(int id, UnitKind kind, int team, Vec2 pos, Fix64 stepPerTick)
        {
            Id = id;
            Kind = kind;
            Team = team;
            Pos = pos;
            PrevPos = pos;
            Target = pos;
            Home = pos;
            StepPerTick = stepPerTick;
            MaxHp = Hp = Defs.Combat(kind).Hp;
        }
    }
}
