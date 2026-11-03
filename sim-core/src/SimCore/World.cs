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
        public const int MaxQueue = 5;
        private const int TicksPerMinute = TicksPerSecond * 60;

        /// <summary>Distance between units when they spread out around a move target.</summary>
        public static readonly Fix64 FormationSpacing = Fix64.One;

        private readonly List<Unit> _units = new List<Unit>();
        private readonly Dictionary<int, Unit> _unitsById = new Dictionary<int, Unit>();
        private readonly List<Building> _buildings = new List<Building>();
        private readonly List<Command> _pending = new List<Command>();
        private int _nextUnitId = 1;
        private int _nextBuildingId = 1;
        private int _incomeProgress; // resource-ticks collected towards the next whole resource

        public MapGrid Map { get; }
        public Fix64 Width { get; }
        public Fix64 Height { get; }
        public int Tick { get; private set; }
        public IReadOnlyList<Unit> Units => _units;
        public IReadOnlyList<Building> Buildings => _buildings;

        /// <summary>Resources in stock. Income arrives automatically (no gatherers).</summary>
        public int Resources { get; private set; } = Economy.StartResources;
        /// <summary>Energy the base can supply (the base itself plus power plants).</summary>
        public int EnergyCapacity { get; private set; }
        /// <summary>Energy the buildings need to work.</summary>
        public int EnergyUsed { get; private set; }
        /// <summary>True when the buildings need more energy than the base supplies; they stop working.</summary>
        public bool EnergyShortage => EnergyUsed > EnergyCapacity;
        /// <summary>Resources per minute at the moment.</summary>
        public int IncomePerMinute { get; private set; }

        public World(MapGrid map)
        {
            Map = map;
            Width = Fix64.FromInt(map.Width);
            Height = Fix64.FromInt(map.Height);
            UpdateEconomy();
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

            var building = new Building(_nextBuildingId++, kind, x, y, def.Width, def.Height, NextOutput(kind));
            for (int cy = y; cy < y + def.Height; cy++)
                for (int cx = x; cx < x + def.Width; cx++)
                    Map.SetBuilding(cx, cy, building.Id);

            _buildings.Add(building);
            UpdateEconomy();
            return building;
        }

        /// <summary>Why a building can or cannot be built at a spot.</summary>
        public BuildCheck CheckBuild(BuildingKind kind, int x, int y)
        {
            if (kind == BuildingKind.Hq) return BuildCheck.NotAllowed;
            BuildingDef def = Defs.Building(kind);
            if (!Map.CanPlace(x, y, def.Width, def.Height)) return BuildCheck.BadGround;
            foreach (Unit u in _units)
            {
                int ux = u.Pos.X.ToInt(), uy = u.Pos.Y.ToInt();
                if (ux >= x && ux < x + def.Width && uy >= y && uy < y + def.Height) return BuildCheck.UnitInWay;
            }
            if (Resources < def.Cost) return BuildCheck.NotEnoughResources;
            return BuildCheck.Ok;
        }

        /// <summary>What the next building of this kind would produce: energy (power plant) or resources per minute (supply center).</summary>
        public int NextOutput(BuildingKind kind)
        {
            if (kind != BuildingKind.PowerPlant && kind != BuildingKind.SupplyCenter) return 0;
            int existing = 0;
            foreach (Building b in _buildings) if (b.Kind == kind) existing++;
            return Economy.Pick(kind == BuildingKind.PowerPlant ? Economy.PowerPlantOutput : Economy.SupplyCenterIncome, existing);
        }

        /// <summary>Recomputes energy balance, which buildings work, and the income.</summary>
        private void UpdateEconomy()
        {
            int capacity = Economy.HqEnergy, used = 0;
            foreach (Building b in _buildings)
            {
                if (b.Kind == BuildingKind.PowerPlant) capacity += b.Output;
                used += Defs.Building(b.Kind).EnergyUse;
            }
            EnergyCapacity = capacity;
            EnergyUsed = used;

            int income = Economy.HqIncomePerMinute;
            foreach (Building b in _buildings)
            {
                // Demo rule: in a shortage every building that needs energy stops.
                b.Powered = !EnergyShortage || Defs.Building(b.Kind).EnergyUse == 0;
                if (b.Kind == BuildingKind.SupplyCenter && b.Powered) income += b.Output;
            }
            IncomePerMinute = income;
        }

        public Building GetBuilding(int id)
        {
            foreach (Building b in _buildings) if (b.Id == id) return b;
            return null;
        }

        public Unit GetUnit(int id) => _unitsById.TryGetValue(id, out Unit u) ? u : null;

        public void Enqueue(Command command) => _pending.Add(command);

        /// <summary>Advance the simulation by one tick.</summary>
        public void Step()
        {
            foreach (Unit u in _units)
                u.PrevPos = u.Pos;

            ApplyPendingCommands();

            ProduceUnits();

            foreach (Unit u in _units)
                MoveUnit(u);

            CollectIncome();
            Tick++;
        }

        /// <summary>Fingerprint of the whole state; clients compare it to detect desyncs.</summary>
        public ulong StateHash()
        {
            StateHasher h = StateHasher.Create();
            h.Add(Tick);
            h.Add(Map.Hash());
            h.Add(Resources);
            h.Add(_incomeProgress);
            h.Add(_nextUnitId);
            foreach (Building b in _buildings)
            {
                h.Add(b.Id);
                h.Add((int)b.Kind);
                h.Add(b.X);
                h.Add(b.Y);
                h.Add(b.Output);
                h.Add(b.Powered ? 1 : 0);
                h.Add(b.Progress);
                h.Add(b.Spawned);
                h.Add(b.HasRally ? 1 : 0);
                if (b.HasRally) h.Add(b.Rally);
                foreach (UnitKind q in b.QueueList) h.Add((int)q);
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
            foreach (Command cmd in _pending)
            {
                switch (cmd)
                {
                    case MoveCommand move: ApplyMove(move); break;
                    case BuildCommand build: ApplyBuild(build); break;
                    case QueueUnitCommand queue: ApplyQueue(queue); break;
                    case CancelQueuedCommand cancel: ApplyCancel(cancel); break;
                    case SetRallyCommand rally: ApplyRally(rally); break;
                }
            }
            _pending.Clear();
        }

        private void ApplyMove(MoveCommand cmd)
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

        private void ApplyBuild(BuildCommand cmd)
        {
            if (CheckBuild(cmd.Kind, cmd.X, cmd.Y) != BuildCheck.Ok) return;

            Resources -= Defs.Building(cmd.Kind).Cost;
            PlaceBuilding(cmd.Kind, cmd.X, cmd.Y);

            // The new building may block routes that were planned before it existed.
            foreach (Unit u in _units)
                if (u.Moving) GoTo(u, u.Target);
        }

        /// <summary>Why a unit can or cannot be queued at a building.</summary>
        public QueueCheck CheckQueue(Building b, UnitKind kind)
        {
            if (b == null || Defs.Unit(kind).Producer != b.Kind) return QueueCheck.NotAllowed;
            if (b.QueueList.Count >= MaxQueue) return QueueCheck.QueueFull;
            if (Resources < Defs.Unit(kind).Cost) return QueueCheck.NotEnoughResources;
            return QueueCheck.Ok;
        }

        private void ApplyQueue(QueueUnitCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (CheckQueue(b, cmd.Kind) != QueueCheck.Ok) return;
            Resources -= Defs.Unit(cmd.Kind).Cost;
            b.QueueList.Add(cmd.Kind);
        }

        private void ApplyCancel(CancelQueuedCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (b == null || cmd.Index < 0 || cmd.Index >= b.QueueList.Count) return;
            Resources += Defs.Unit(b.QueueList[cmd.Index]).Cost;
            b.QueueList.RemoveAt(cmd.Index);
            if (cmd.Index == 0) b.Progress = 0;
        }

        private void ApplyRally(SetRallyCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (b == null || Defs.Produces(b.Kind).Length == 0) return;
            b.HasRally = true;
            b.Rally = ClampToMap(cmd.Point);
        }

        /// <summary>Advances every working production building; finished units appear at its door.</summary>
        private void ProduceUnits()
        {
            foreach (Building b in _buildings)
            {
                if (b.QueueList.Count == 0 || !b.Powered) continue;

                UnitKind kind = b.QueueList[0];
                if (b.Progress < Defs.Unit(kind).BuildSeconds * TicksPerSecond)
                {
                    b.Progress++;
                    continue;
                }

                // Door is on the low-y side, middle of the footprint.
                var door = Pathfinder.NearestWalkable(Map, b.X + b.Width / 2, b.Y - 1);
                if (door == null) continue; // nowhere to stand: wait

                // Spread new units a little so they do not all sit on one point.
                Fix64 offset = Fix64.FromFraction((b.Spawned % 4) * 3 - 4, 10);
                var pos = new Vec2(Fix64.FromInt(door.Value.x) + Fix64.Half + offset, Fix64.FromInt(door.Value.y) + Fix64.Half);
                b.Spawned++;
                b.QueueList.RemoveAt(0);
                b.Progress = 0;

                Unit unit = SpawnUnit(kind, pos);
                if (b.HasRally) GoTo(unit, b.Rally);
            }
        }

        /// <summary>Adds the income for this tick. Whole resources only, so no fractions are needed.</summary>
        private void CollectIncome()
        {
            _incomeProgress += IncomePerMinute;
            while (_incomeProgress >= TicksPerMinute)
            {
                _incomeProgress -= TicksPerMinute;
                Resources++;
            }
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
