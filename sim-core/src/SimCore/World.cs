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
        public const int TeamPlayer = 0;
        public const int TeamEnemy = 1;
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
        private readonly List<ShotEvent> _shots = new List<ShotEvent>();

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
            return Spawn(kind, position, Defs.Unit(kind).Speed, TeamPlayer);
        }

        public Unit SpawnUnit(UnitKind kind, Vec2 position, int team)
        {
            return Spawn(kind, position, Defs.Unit(kind).Speed, team);
        }

        /// <summary>Makes a unit a computer-controlled guard around where it stands; with patrol points it walks between them.</summary>
        public void SetGuard(Unit unit, Vec2[] patrol = null)
        {
            unit.Guard = true;
            unit.Home = unit.Pos;
            unit.Patrol = patrol;
            unit.PatrolIndex = 0;
        }

        /// <param name="speedPerSecond">Map units per second.</param>
        public Unit SpawnUnit(Vec2 position, Fix64 speedPerSecond)
        {
            return Spawn(UnitKind.Rifleman, position, speedPerSecond, TeamPlayer);
        }

        private Unit Spawn(UnitKind kind, Vec2 position, Fix64 speedPerSecond, int team)
        {
            Fix64 step = Fix64.FromRaw(speedPerSecond.Raw / TicksPerSecond);
            var unit = new Unit(_nextUnitId++, kind, team, ClampToMap(position), step);
            _units.Add(unit);
            _unitsById[unit.Id] = unit;
            return unit;
        }

        /// <summary>Places a building with its top-left corner on cell (x, y). Returns null if it does not fit there.</summary>
        public Building PlaceBuilding(BuildingKind kind, int x, int y, int team = TeamPlayer)
        {
            BuildingDef def = Defs.Building(kind);
            if (!Map.CanPlace(x, y, def.Width, def.Height)) return null;

            var building = new Building(_nextBuildingId++, kind, team, x, y, def.Width, def.Height, team == TeamPlayer ? NextOutput(kind) : 0);
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
            foreach (Building b in _buildings) if (b.Kind == kind && b.Team == TeamPlayer) existing++;
            return Economy.Pick(kind == BuildingKind.PowerPlant ? Economy.PowerPlantOutput : Economy.SupplyCenterIncome, existing);
        }

        /// <summary>Recomputes energy balance, which buildings work, and the income.</summary>
        private void UpdateEconomy()
        {
            int capacity = Economy.HqEnergy, used = 0;
            foreach (Building b in _buildings)
            {
                if (b.Team != TeamPlayer) continue;
                if (b.Kind == BuildingKind.PowerPlant) capacity += b.Output;
                used += Defs.Building(b.Kind).EnergyUse;
            }
            EnergyCapacity = capacity;
            EnergyUsed = used;

            int income = Economy.HqIncomePerMinute;
            foreach (Building b in _buildings)
            {
                if (b.Team != TeamPlayer) continue;
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
            Fight();

            foreach (Unit u in _units)
                MoveUnit(u);

            RemoveDestroyed();
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
                h.Add(b.Team);
                h.Add(b.Hp);
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
                h.Add(u.Team);
                h.Add(u.Hp);
                h.Add((int)u.AttackKind);
                h.Add(u.AttackId);
                h.Add(u.Cooldown);
                h.Add(u.Marching ? 1 : 0);
                h.Add(u.Facing);
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
                    case AttackCommand attack: ApplyAttack(attack); break;
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

            Fix64 cap = SlowestStep(units);
            Vec2[] offsets = FormationOffsets(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Team != TeamPlayer) continue;
                ClearTarget(units[i]); // a move order cancels an attack order
                units[i].SpeedCap = cap;
                GoTo(units[i], ClampToMap(cmd.Target + offsets[i]));
                units[i].Marching = units[i].Moving;
            }
        }

        /// <summary>The slowest step among the player's units in the list: a group travels together at that pace.</summary>
        private static Fix64 SlowestStep(List<Unit> units)
        {
            Fix64 slowest = default;
            bool any = false;
            foreach (Unit u in units)
            {
                if (u.Team != TeamPlayer) continue;
                if (!any || u.StepPerTick < slowest) { slowest = u.StepPerTick; any = true; }
            }
            return slowest;
        }

        private void ApplyAttack(AttackCommand cmd)
        {
            if (!TargetPosition(cmd.TargetKind, cmd.TargetId, TeamEnemy, out _)) return;

            var units = new List<Unit>(cmd.UnitIds.Length);
            foreach (int id in cmd.UnitIds)
            {
                Unit found = GetUnit(id);
                if (found != null) units.Add(found);
            }
            Fix64 cap = SlowestStep(units);

            foreach (Unit u in units)
            {
                if (u.Team != TeamPlayer) continue;
                u.SpeedCap = cap;
                u.Marching = false;
                u.AttackKind = cmd.TargetKind;
                u.AttackId = cmd.TargetId;
                u.Ordered = true;
            }
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
                if (b.Team != TeamPlayer || b.QueueList.Count == 0 || !b.Powered) continue;

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

        // ------------------------------------------------------------------ combat

        /// <summary>Hands over the shots fired since the last call (for drawing tracers); not part of the game state.</summary>
        public void DrainShots(List<ShotEvent> into)
        {
            into.AddRange(_shots);
            _shots.Clear();
        }

        public int CountAlive(int team)
        {
            int n = 0;
            foreach (Unit u in _units) if (u.Team == team && u.Hp > 0) n++;
            foreach (Building b in _buildings) if (b.Team == team && b.Hp > 0) n++;
            return n;
        }

        public bool HasBuilding(BuildingKind kind, int team)
        {
            foreach (Building b in _buildings) if (b.Kind == kind && b.Team == team && b.Hp > 0) return true;
            return false;
        }

        private static void ClearTarget(Unit u)
        {
            u.AttackKind = AttackKind.None;
            u.AttackId = 0;
            u.Ordered = false;
        }

        /// <summary>Position of the target (nearest point for buildings) if it exists, is alive and belongs to the given team.</summary>
        private bool TargetPosition(AttackKind kind, int id, int team, out Vec2 pos)
        {
            pos = default;
            if (kind == AttackKind.Unit)
            {
                Unit t = GetUnit(id);
                if (t == null || t.Hp <= 0 || t.Team != team) return false;
                pos = t.Pos;
                return true;
            }
            if (kind == AttackKind.Building)
            {
                Building t = GetBuilding(id);
                if (t == null || t.Hp <= 0 || t.Team != team) return false;
                pos = new Vec2(Fix64.FromInt(t.X) + Fix64.FromInt(t.Width) * Fix64.Half, Fix64.FromInt(t.Y) + Fix64.FromInt(t.Height) * Fix64.Half);
                return true;
            }
            return false;
        }

        /// <summary>Distance-squared from a point to a building's footprint (0 when inside).</summary>
        private static Fix64 SqrDistanceToBuilding(Vec2 p, Building b, out Vec2 nearest)
        {
            nearest = new Vec2(
                Fix64.Clamp(p.X, Fix64.FromInt(b.X), Fix64.FromInt(b.X + b.Width)),
                Fix64.Clamp(p.Y, Fix64.FromInt(b.Y), Fix64.FromInt(b.Y + b.Height)));
            return (nearest - p).SqrLength;
        }

        private void Fight()
        {
            foreach (Unit u in _units)
            {
                if (u.Hp <= 0) continue;
                if (u.Cooldown > 0) u.Cooldown--;
                FightStep(u);
            }
        }

        private void FightStep(Unit u)
        {
            CombatDef def = Defs.Combat(u.Kind);
            int enemyTeam = 1 - u.Team;

            if (u.Marching && u.Moving)
            {
                MarchingFight(u, def, enemyTeam);
                return;
            }

            // Drop a target that is gone.
            Vec2 targetPos = default;
            if (u.AttackKind != AttackKind.None && !TargetPosition(u.AttackKind, u.AttackId, enemyTeam, out targetPos))
                ClearTarget(u);

            // A unit hitting a building answers enemy units that come into its reach (they are the ones shooting back).
            if (u.AttackKind == AttackKind.Building && (Tick + u.Id) % 5 == 0 && EnemyUnitInReach(u, def, enemyTeam))
            {
                ClearTarget(u);
                Stop(u); // stand and fight: the next step picks the nearest enemy unit
            }

            // Look for something to shoot: units that stand still (and computer guards, even on patrol) notice enemies.
            if (u.AttackKind == AttackKind.None && (!u.Moving || u.Guard) && (Tick + u.Id) % 5 == 0)
                Acquire(u, def, enemyTeam);

            if (u.AttackKind == AttackKind.None)
            {
                if (u.Guard) GuardIdle(u);
                return;
            }

            // Re-read position of a freshly acquired target.
            TargetPosition(u.AttackKind, u.AttackId, enemyTeam, out targetPos);

            Vec2 aim = targetPos;
            Fix64 sqrDist;
            if (u.AttackKind == AttackKind.Building)
                sqrDist = SqrDistanceToBuilding(u.Pos, GetBuilding(u.AttackId), out aim);
            else
                sqrDist = (targetPos - u.Pos).SqrLength;

            if (sqrDist <= def.Range * def.Range)
            {
                if (u.Moving) Stop(u);
                u.AimPoint = aim;
                if ((aim - u.Pos).SqrLength.Raw > 0) u.Facing = (aim - u.Pos).Normalized(); // a standing unit turns to its target
                if (u.Cooldown == 0) Fire(u, def, aim);
                return;
            }

            // Out of range: give up on automatic targets that got away, otherwise chase.
            if (!u.Ordered)
            {
                Fix64 leash = def.Sight * Fix64.FromFraction(3, 2);
                bool tooFar = sqrDist > leash * leash;
                if (u.Guard && (u.Pos - u.Home).SqrLength > GuardLeash * GuardLeash) tooFar = true;
                if (tooFar) { ClearTarget(u); return; }
            }
            if ((Tick + u.Id) % 10 == 0) GoTo(u, targetPos); // re-plan twice a second at most
        }

        /// <summary>
        /// A unit on a move order: it keeps walking, and shoots at anything inside its range and firing arc.
        /// It never stops or chases; a target that leaves the arc is simply dropped.
        /// </summary>
        private void MarchingFight(Unit u, CombatDef def, int enemyTeam)
        {
            Vec2 aim = default;
            Fix64 range2 = def.Range * def.Range;

            if (u.AttackKind != AttackKind.None)
            {
                if (!ReachablePoint(u, def, range2, out aim)) ClearTarget(u);
            }

            if (u.AttackKind == AttackKind.None && (Tick + u.Id) % 3 == 0)
            {
                // Nearest enemy unit in range and arc, else nearest building.
                Fix64 best = range2 + Fix64.One;
                foreach (Unit t in _units)
                {
                    if (t.Team != enemyTeam || t.Hp <= 0) continue;
                    Fix64 dist = (t.Pos - u.Pos).SqrLength;
                    if (dist <= range2 && dist < best && InArc(u, def, t.Pos)) { best = dist; u.AttackKind = AttackKind.Unit; u.AttackId = t.Id; }
                }
                if (u.AttackKind == AttackKind.None)
                {
                    foreach (Building t in _buildings)
                    {
                        if (t.Team != enemyTeam || t.Hp <= 0) continue;
                        Fix64 dist = SqrDistanceToBuilding(u.Pos, t, out Vec2 nearest);
                        if (dist <= range2 && dist < best && InArc(u, def, nearest)) { best = dist; u.AttackKind = AttackKind.Building; u.AttackId = t.Id; }
                    }
                }
                if (u.AttackKind != AttackKind.None && !ReachablePoint(u, def, range2, out aim)) ClearTarget(u);
            }

            if (u.AttackKind != AttackKind.None)
            {
                u.AimPoint = aim;
                if (u.Cooldown == 0) Fire(u, def, aim);
            }
        }

        /// <summary>True if the current target exists and is inside the unit's range and firing arc; aim = where to shoot.</summary>
        private bool ReachablePoint(Unit u, CombatDef def, Fix64 range2, out Vec2 aim)
        {
            aim = default;
            int enemyTeam = 1 - u.Team;
            if (!TargetPosition(u.AttackKind, u.AttackId, enemyTeam, out Vec2 pos)) return false;

            Fix64 dist;
            aim = pos;
            if (u.AttackKind == AttackKind.Building) dist = SqrDistanceToBuilding(u.Pos, GetBuilding(u.AttackId), out aim);
            else dist = (pos - u.Pos).SqrLength;
            return dist <= range2 && InArc(u, def, aim);
        }

        /// <summary>Is the point inside the unit's firing arc (centred on the way it faces)?</summary>
        private static bool InArc(Unit u, CombatDef def, Vec2 point)
        {
            Vec2 to = point - u.Pos;
            if (to.SqrLength.Raw == 0) return true;
            return u.Facing.Dot(to.Normalized()) >= def.CosHalfArc;
        }

        private bool EnemyUnitInReach(Unit u, CombatDef def, int enemyTeam)
        {
            Fix64 reach = def.Range * Fix64.FromFraction(3, 2);
            Fix64 reach2 = reach * reach;
            foreach (Unit t in _units)
                if (t.Team == enemyTeam && t.Hp > 0 && (t.Pos - u.Pos).SqrLength <= reach2) return true;
            return false;
        }

        private static readonly Fix64 GuardLeash = Fix64.FromInt(14);

        private void Acquire(Unit u, CombatDef def, int enemyTeam)
        {
            Fix64 sight2 = def.Sight * def.Sight;
            Fix64 best = sight2 + Fix64.One;
            Unit bestUnit = null;
            foreach (Unit t in _units)
            {
                if (t.Team != enemyTeam || t.Hp <= 0) continue;
                Fix64 d = (t.Pos - u.Pos).SqrLength;
                if (d <= sight2 && d < best) { best = d; bestUnit = t; }
            }
            if (bestUnit != null)
            {
                u.SpeedCap = default;
                u.AttackKind = AttackKind.Unit;
                u.AttackId = bestUnit.Id;
                return;
            }

            Building bestBuilding = null;
            best = sight2 + Fix64.One;
            foreach (Building t in _buildings)
            {
                if (t.Team != enemyTeam || t.Hp <= 0) continue;
                Fix64 d = SqrDistanceToBuilding(u.Pos, t, out _);
                if (d <= sight2 && d < best) { best = d; bestBuilding = t; }
            }
            if (bestBuilding != null)
            {
                u.SpeedCap = default;
                u.AttackKind = AttackKind.Building;
                u.AttackId = bestBuilding.Id;
            }
        }

        /// <summary>A guard with nothing to shoot: walk to the next patrol point, or back home.</summary>
        private void GuardIdle(Unit u)
        {
            if (u.Moving) return;
            if (u.Patrol != null && u.Patrol.Length > 0)
            {
                u.PatrolIndex = (u.PatrolIndex + 1) % u.Patrol.Length;
                GoTo(u, u.Patrol[u.PatrolIndex]);
            }
            else if ((u.Pos - u.Home).SqrLength > Fix64.One)
            {
                GoTo(u, u.Home);
            }
        }

        private void Fire(Unit u, CombatDef def, Vec2 aim)
        {
            u.Cooldown = def.CooldownTicks;
            u.LastShotTick = Tick;
            _shots.Add(new ShotEvent(u.Pos, aim, u.Kind, u.Team));
            if (_shots.Count > 512) _shots.RemoveRange(0, 256); // nobody is listening: keep it small

            if (u.AttackKind == AttackKind.Unit) GetUnit(u.AttackId).Hp -= def.Damage;
            else GetBuilding(u.AttackId).Hp -= def.Damage;
        }

        /// <summary>Removes everything whose health ran out.</summary>
        private void RemoveDestroyed()
        {
            bool buildingLost = false;
            for (int i = _buildings.Count - 1; i >= 0; i--)
            {
                Building b = _buildings[i];
                if (b.Hp > 0) continue;
                for (int cy = b.Y; cy < b.Y + b.Height; cy++)
                    for (int cx = b.X; cx < b.X + b.Width; cx++)
                        Map.SetBuilding(cx, cy, 0);
                _buildings.RemoveAt(i);
                buildingLost = true;
            }
            if (buildingLost) UpdateEconomy();

            for (int i = _units.Count - 1; i >= 0; i--)
            {
                if (_units[i].Hp > 0) continue;
                _unitsById.Remove(_units[i].Id);
                _units.RemoveAt(i);
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
            u.Marching = false;
            if (u.AttackKind == AttackKind.None) u.SpeedCap = default;
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
            if (u.SpeedCap.Raw > 0 && u.SpeedCap < step) step = u.SpeedCap;

            if (delta.SqrLength <= step * step)
            {
                u.Pos = waypoint;
                u.PathIndex++;
                if (u.PathIndex >= u.Path.Length) Stop(u);
            }
            else
            {
                Vec2 dir = delta.Normalized();
                u.Pos = u.Pos + dir * step;
                u.Facing = dir;
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
