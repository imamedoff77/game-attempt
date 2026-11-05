using System.Collections.Generic;

namespace SimCore
{
    public sealed class Building
    {
        public int Id { get; }
        public BuildingKind Kind { get; }
        /// <summary>0 = player, 1 = enemy.</summary>
        public int Team { get; }

        public int Hp { get; internal set; }
        /// <summary>Damage dealt this tick that has not been applied yet (see World.ApplyIncomingDamage). Always 0 between ticks.</summary>
        internal int Incoming;
        /// <summary>Counts the team's units (buildings) in creation order; staggers periodic work. Not part of the hash: it follows from the history.</summary>
        internal int Phase;
        public int MaxHp { get; internal set; }

        /// <summary>Top-left cell of the footprint, and its size in cells.</summary>
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>Energy produced (power plant) or resources per minute produced (supply center), fixed when built.</summary>
        public int Output { get; }

        /// <summary>Ticks of construction done so far; the building works once this reaches <see cref="ConstructionTicks"/>.</summary>
        public int BuildProgress { get; internal set; }
        /// <summary>Ticks until a defence building can shoot again.</summary>
        internal int Cooldown;
        public int ConstructionTicks { get; internal set; }
        /// <summary>True while the building is still being built: it uses no energy, produces nothing and gives no income.</summary>
        public bool UnderConstruction => BuildProgress < ConstructionTicks;

        /// <summary>False while the base lacks the energy to run this building.</summary>
        public bool Powered { get; internal set; } = true;

        /// <summary>Units waiting to be produced; the first one is being built.</summary>
        public IReadOnlyList<UnitKind> Queue => QueueList;
        internal readonly List<UnitKind> QueueList = new List<UnitKind>();

        /// <summary>Ticks of work done on the first queued unit.</summary>
        public int Progress { get; internal set; }

        /// <summary>Where new units walk to after they appear (only when HasRally).</summary>
        public bool HasRally { get; internal set; }
        public Vec2 Rally { get; internal set; }

        internal int Spawned;

        internal Building(int id, BuildingKind kind, int team, int x, int y, int width, int height, int output)
        {
            Id = id; Kind = kind; Team = team; MaxHp = Hp = Defs.BuildingHp(kind); X = x; Y = y; Width = width; Height = height; Output = output;
        }

        public bool Contains(int cellX, int cellY) =>
            cellX >= X && cellX < X + Width && cellY >= Y && cellY < Y + Height;
    }
}
