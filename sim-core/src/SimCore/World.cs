using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// The whole game state and its rules. Contains no rendering and no floating point,
    /// so every device computes exactly the same result from the same commands.
    /// </summary>
    public sealed class World
    {
        public const int TicksPerSecond = 20;

        /// <summary>Distance between units when they spread out around a move target.</summary>
        public static readonly Fix64 FormationSpacing = Fix64.One;

        private readonly List<Unit> _units = new List<Unit>();
        private readonly Dictionary<int, Unit> _unitsById = new Dictionary<int, Unit>();
        private readonly List<Building> _buildings = new List<Building>();
        private readonly List<MoveCommand> _pending = new List<MoveCommand>();
        private int _nextUnitId = 1;
        private int _nextBuildingId = 1;

        public MapGrid Map { get; }
        public Fix64 Width { get; }
        public Fix64 Height { get; }
        public int Tick { get; private set; }
        public IReadOnlyList<Unit> Units => _units;
        public IReadOnlyList<Building> Buildings => _buildings;

        public World(MapGrid map)
        {
            Map = map;
            Width = Fix64.FromInt(map.Width);
            Height = Fix64.FromInt(map.Height);
        }

        /// <summary>All-sand map of the given size.</summary>
        public World(Fix64 width, Fix64 height) : this(new MapGrid(width.ToInt(), height.ToInt()))
        {
        }

        public Unit SpawnUnit(UnitKind kind, Vec2 position)
        {
            return Spawn(kind, position, Defs.Unit(kind).Speed);
        }

        /// <param name="speedPerSecond">Map units per second.</param>
        public Unit SpawnUnit(Vec2 position, Fix64 speedPerSecond)
        {
            return Spawn(UnitKind.Rifleman, position, speedPerSecond);
        }

        private Unit Spawn(UnitKind kind, Vec2 position, Fix64 speedPerSecond)
        {
            Fix64 step = Fix64.FromRaw(speedPerSecond.Raw / TicksPerSecond);
            var unit = new Unit(_nextUnitId++, kind, ClampToMap(position), step);
            _units.Add(unit);
            _unitsById[unit.Id] = unit;
            return unit;
        }

        /// <summary>Places a building with its top-left corner on cell (x, y). Returns null if it does not fit there.</summary>
        public Building PlaceBuilding(BuildingKind kind, int x, int y)
        {
            BuildingDef def = Defs.Building(kind);
            if (!Map.CanPlace(x, y, def.Width, def.Height)) return null;

            var building = new Building(_nextBuildingId++, kind, x, y, def.Width, def.Height);
            for (int cy = y; cy < y + def.Height; cy++)
                for (int cx = x; cx < x + def.Width; cx++)
                    Map.SetBuilding(cx, cy, building.Id);

            _buildings.Add(building);
            return building;
        }

        public Unit GetUnit(int id) => _unitsById.TryGetValue(id, out Unit u) ? u : null;

        public void Enqueue(MoveCommand command) => _pending.Add(command);

        /// <summary>Advance the simulation by one tick.</summary>
        public void Step()
        {
            foreach (Unit u in _units)
                u.PrevPos = u.Pos;

            ApplyPendingCommands();

            foreach (Unit u in _units)
                MoveUnit(u);

            Tick++;
        }

        /// <summary>Fingerprint of the whole state; clients compare it to detect desyncs.</summary>
        public ulong StateHash()
        {
            StateHasher h = StateHasher.Create();
            h.Add(Tick);
            h.Add(Map.Hash());
            foreach (Building b in _buildings)
            {
                h.Add(b.Id);
                h.Add((int)b.Kind);
                h.Add(b.X);
                h.Add(b.Y);
            }
            foreach (Unit u in _units)
            {
                h.Add(u.Id);
                h.Add((int)u.Kind);
                h.Add(u.Pos);
                h.Add(u.Target);
                h.Add(u.Moving ? 1 : 0);
                h.Add(u.PathIndex);
            }
            return h.Value;
        }

        /// <summary>Slot offsets for <paramref name="count"/> units, centred on (0,0), laid out in a grid.</summary>
        public static Vec2[] FormationOffsets(int count)
        {
            var offsets = new Vec2[count];
            if (count == 0) return offsets;

            int columns = 1;
            while (columns * columns < count) columns++;
            int rows = (count + columns - 1) / columns;

            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                // (2*col - (columns-1)) * spacing / 2 centres the grid on the target.
                Fix64 x = FormationSpacing * Fix64.FromInt(col * 2 - (columns - 1)) * Fix64.Half;
                Fix64 y = FormationSpacing * Fix64.FromInt(row * 2 - (rows - 1)) * Fix64.Half;
                offsets[i] = new Vec2(x, y);
            }
            return offsets;
        }

        private void ApplyPendingCommands()
        {
            foreach (MoveCommand cmd in _pending)
            {
                var units = new List<Unit>(cmd.UnitIds.Length);
                foreach (int id in cmd.UnitIds)
                {
                    Unit u = GetUnit(id);
                    if (u != null) units.Add(u);
                }

                Vec2[] offsets = FormationOffsets(units.Count);
                for (int i = 0; i < units.Count; i++)
                    GoTo(units[i], ClampToMap(cmd.Target + offsets[i]));
            }
            _pending.Clear();
        }

        /// <summary>Plans a route for the unit. Blocked destinations are moved to the nearest free cell; unreachable ones are ignored.</summary>
        private void GoTo(Unit u, Vec2 target)
        {
            int gx = target.X.ToInt(), gy = target.Y.ToInt();
            var free = Pathfinder.NearestWalkable(Map, gx, gy);
            if (free == null) { Stop(u); return; }

            if (free.Value.x != gx || free.Value.y != gy)
            {
                gx = free.Value.x;
                gy = free.Value.y;
                target = new Vec2(Fix64.FromInt(gx) + Fix64.Half, Fix64.FromInt(gy) + Fix64.Half);
            }

            var cells = Pathfinder.FindPath(Map, u.Pos.X.ToInt(), u.Pos.Y.ToInt(), gx, gy);
            if (cells == null) { Stop(u); return; }

            // Skip the start cell; walk through the other cells' centres and finish exactly on the target.
            var waypoints = new Vec2[cells.Count == 1 ? 1 : cells.Count - 1];
            for (int i = 1; i < cells.Count; i++)
                waypoints[i - 1] = new Vec2(Fix64.FromInt(cells[i].x) + Fix64.Half, Fix64.FromInt(cells[i].y) + Fix64.Half);
            waypoints[waypoints.Length - 1] = target;

            u.Path = waypoints;
            u.PathIndex = 0;
            u.Target = target;
            u.Moving = true;
        }

        private static void Stop(Unit u)
        {
            u.Path = null;
            u.PathIndex = 0;
            u.Moving = false;
        }

        private static void MoveUnit(Unit u)
        {
            if (!u.Moving) return;

            Vec2 waypoint = u.Path[u.PathIndex];
            Vec2 delta = waypoint - u.Pos;
            Fix64 step = u.StepPerTick;

            if (delta.SqrLength <= step * step)
            {
                u.Pos = waypoint;
                u.PathIndex++;
                if (u.PathIndex >= u.Path.Length) Stop(u);
            }
            else
            {
                u.Pos = u.Pos + delta.Normalized() * step;
            }
        }

        private Vec2 ClampToMap(Vec2 p)
        {
            Fix64 margin = Fix64.Half;
            return new Vec2(
                Fix64.Clamp(p.X, margin, Width - margin),
                Fix64.Clamp(p.Y, margin, Height - margin));
        }
    }
}
