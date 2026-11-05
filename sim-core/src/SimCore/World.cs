using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// The whole game state and its rules. Contains no rendering and no floating point,
    /// so every device computes exactly the same result from the same commands.
    /// </summary>
    public sealed partial class World
    {
        public const int TicksPerSecond = 20;
        public const int MaxQueue = 5;
        public const int TeamPlayer = 0;
        public const int TeamEnemy = 1;
        /// <summary>Number of teams (1v1).</summary>
        public const int TeamCount = 2;
        private const int TicksPerMinute = TicksPerSecond * 60;

        /// <summary>Distance between units when they spread out around a move target.</summary>
        public static readonly Fix64 FormationSpacing = Fix64.One;

        private readonly List<Unit> _units = new List<Unit>();
        private readonly Dictionary<int, Unit> _unitsById = new Dictionary<int, Unit>();
        private readonly List<Building> _buildings = new List<Building>();
        private readonly List<Command> _pending = new List<Command>();
        private int _nextUnitId = 1;
        private int _nextBuildingId = 1;
        private readonly PlayerState[] _players = new PlayerState[TeamCount];
        private readonly List<ShotEvent> _shots = new List<ShotEvent>();

        public MapGrid Map { get; }
        public Fix64 Width { get; }
        public Fix64 Height { get; }
        public int Tick { get; private set; }
        public IReadOnlyList<Unit> Units => _units;
        public IReadOnlyList<Building> Buildings => _buildings;

        /// <summary>Economy of one team (resources, energy, income).</summary>
        public PlayerState Player(int team) => _players[team];

        // Shortcuts for the player's own team (team 0), used by the single-player demo UI and tests.
        /// <summary>Resources in stock. Income arrives automatically (no gatherers).</summary>
        public int Resources => _players[TeamPlayer].Resources;
        /// <summary>Energy the base can supply (the base itself plus power plants).</summary>
        public int EnergyCapacity => _players[TeamPlayer].EnergyCapacity;
        /// <summary>Energy the buildings need to work.</summary>
        public int EnergyUsed => _players[TeamPlayer].EnergyUsed;
        /// <summary>True when the buildings need more energy than the base supplies; they stop working.</summary>
        public bool EnergyShortage => _players[TeamPlayer].EnergyShortage;
        /// <summary>Resources per minute at the moment.</summary>
        public int IncomePerMinute => _players[TeamPlayer].IncomePerMinute;

        public World(MapGrid map)
        {
            Map = map;
            Width = Fix64.FromInt(map.Width);
            Height = Fix64.FromInt(map.Height);
            for (int t = 0; t < TeamCount; t++)
            {
                _players[t] = new PlayerState();
                UpdateEconomy(t);
            }
        }

        /// <summary>All-sand map of the given size.</summary>
        public World(Fix64 width, Fix64 height) : this(new MapGrid(width.ToInt(), height.ToInt()))
        {
        }

        // Numbers each team's units and buildings in the order they appear. Periodic work (target search, re-planning, tower fire)
        // is staggered by this number instead of the global id, so the two sides of a mirrored match get the same timing.
        private readonly int[] _unitsSpawned = new int[2];
        private readonly int[] _buildingsPlaced = new int[2];

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
            unit.MaxHp = unit.Hp = EffectiveMaxHp(team, kind);
            unit.Phase = _unitsSpawned[team & 1]++;
            _units.Add(unit);
            _unitsById[unit.Id] = unit;
            RecountPop(team);
            return unit;
        }

        /// <summary>
        /// Places a building with its top-left corner on cell (x, y). Returns null if it does not fit there.
        /// Scenarios place ready buildings; a building ordered by a player starts <paramref name="underConstruction"/>.
        /// </summary>
        public Building PlaceBuilding(BuildingKind kind, int x, int y, int team = TeamPlayer, bool underConstruction = false)
        {
            BuildingDef def = Defs.Building(kind);
            if (!Map.CanPlace(x, y, def.Width, def.Height)) return null;

            var building = new Building(_nextBuildingId++, kind, team, x, y, def.Width, def.Height, NextOutput(kind, team));
            if (underConstruction && def.ConstructionSeconds > 0)
            {
                building.ConstructionTicks = def.ConstructionSeconds * TicksPerSecond;
                building.Hp = System.Math.Max(1, building.MaxHp / 10);
            }
            for (int cy = y; cy < y + def.Height; cy++)
                for (int cx = x; cx < x + def.Width; cx++)
                    Map.SetBuilding(cx, cy, building.Id);

            building.Phase = _buildingsPlaced[team & 1]++;
            _buildings.Add(building);
            UpdateEconomy(team);
            return building;
        }

        /// <summary>Why a building can or cannot be built at a spot.</summary>
        public BuildCheck CheckBuild(BuildingKind kind, int x, int y, int team = TeamPlayer)
        {
            if (kind == BuildingKind.Hq) return BuildCheck.NotAllowed;
            BuildingDef def = Defs.Building(kind);
            if (_players[team].BaseLevel < def.RequiredLevel) return BuildCheck.LevelTooLow;
            if (!Map.CanPlace(x, y, def.Width, def.Height)) return BuildCheck.BadGround;
            foreach (Unit u in _units)
            {
                int ux = u.Pos.X.ToInt(), uy = u.Pos.Y.ToInt();
                if (ux >= x && ux < x + def.Width && uy >= y && uy < y + def.Height) return BuildCheck.UnitInWay;
            }
            if (_players[team].Resources < def.Cost) return BuildCheck.NotEnoughResources;
            return BuildCheck.Ok;
        }

        /// <summary>What the next building of this kind would produce: energy (power plant) or resources per minute (supply center).</summary>
        public int NextOutput(BuildingKind kind, int team = TeamPlayer)
        {
            if (kind != BuildingKind.PowerPlant && kind != BuildingKind.SupplyCenter) return 0;
            int existing = 0;
            foreach (Building b in _buildings) if (b.Kind == kind && b.Team == team) existing++;
            return Economy.Pick(kind == BuildingKind.PowerPlant ? Economy.PowerPlantOutput : Economy.SupplyCenterIncome, existing);
        }

        /// <summary>Recomputes one team's energy balance, which of its buildings work, and its income.</summary>
        private void UpdateEconomy(int team)
        {
            PlayerState p = _players[team];
            int capacity = Economy.HqEnergy, used = 0;
            foreach (Building b in _buildings)
            {
                if (b.Team != team || b.UnderConstruction) continue; // a building under construction neither gives nor needs anything
                if (b.Kind == BuildingKind.PowerPlant) capacity += b.Output;
                used += Defs.Building(b.Kind).EnergyUse;
            }
            p.EnergyCapacity = capacity;
            p.EnergyUsed = used;

            int income = Economy.HqIncomePerMinute;
            foreach (Building b in _buildings)
            {
                if (b.Team != team) continue;
                // Demo rule: in a shortage every building that needs energy stops.
                b.Powered = b.UnderConstruction || !p.EnergyShortage || Defs.Building(b.Kind).EnergyUse == 0;
                if (b.Kind == BuildingKind.SupplyCenter && b.Powered) income += b.Output;
            }
            p.IncomePerMinute = income;
        }

        public Building GetBuilding(int id)
        {
            foreach (Building b in _buildings) if (b.Id == id) return b;
            return null;
        }

        /// <summary>Adds (or removes, when negative) resources of a team outside the command flow: for scenarios and tests, never for a player's action.</summary>
        public void GiveResources(int team, int amount) => _players[team].Resources += amount;

        /// <summary>Scenario set-up: a team that starts at a higher base level (more buildings and units open to it, bigger unit limit).</summary>
        public void SetBaseLevel(int team, int level) => _players[team].BaseLevel = level < 1 ? 1 : level > Economy.MaxBaseLevel ? Economy.MaxBaseLevel : level;

        /// <summary>Scenario set-up: a building that starts weaker (or stronger) than the standard health, e.g. a damaged enemy outpost.</summary>
        public void SetBuildingHealth(int buildingId, int hp)
        {
            Building b = GetBuilding(buildingId);
            if (b == null) return;
            b.MaxHp = hp;
            b.Hp = hp;
        }

        /// <summary>Takes health off a unit outside of combat (scenarios and tests). A unit at 0 health is removed on the next step.</summary>
        public void DamageUnit(int unitId, int amount)
        {
            Unit u = GetUnit(unitId);
            if (u != null) u.Hp -= amount;
        }

        public Unit GetUnit(int id) => _unitsById.TryGetValue(id, out Unit u) ? u : null;

        /// <summary>Queues a command for the next tick. It acts for team 0 unless <see cref="Command.Team"/> was set.</summary>
        public void Enqueue(Command command) => _pending.Add(command);

        /// <summary>Queues a command that acts for the given team (the network layer stamps the sender's team here).</summary>
        public void Enqueue(Command command, int team)
        {
            command.Team = team;
            _pending.Add(command);
        }

        /// <summary>Advance the simulation by one tick.</summary>
        public void Step()
        {
            foreach (Unit u in _units)
                u.PrevPos = u.Pos;

            ApplyPendingCommands();

            Construct();
            ProduceUnits();
            Fight();
            DefenceAndRepair();
            ApplyIncomingDamage();

            foreach (Unit u in _units)
                MoveUnit(u);
            SeparateUnits();

            RemoveDestroyed();
            CollectIncome();
            for (int t = 0; t < TeamCount; t++) RecountPop(t);
            Tick++;
        }

        /// <summary>Fingerprint of the whole state; clients compare it to detect desyncs.</summary>
        public ulong StateHash()
        {
            StateHasher h = StateHasher.Create();
            h.Add(Tick);
            h.Add(Map.Hash());
            foreach (PlayerState p in _players)
            {
                h.Add(p.Resources);
                h.Add(p.IncomeProgress);
                h.Add(p.BaseLevel);
                for (int k = 0; k < p.UpgradeTiers.Length; k++) h.Add(p.UpgradeTiers[k]);
                h.Add((int)p.ResearchKind);
                h.Add(p.ResearchTicksLeft);
                h.Add(p.UpgradeTicksLeft);
                for (int k = 0; k < p.KitBonusMilli.Length; k++) if (p.KitBonusMilli[k] != 0) { h.Add(k); h.Add(p.KitBonusMilli[k]); } // only when used, so plain matches keep their hash
            }
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
                h.Add(b.BuildProgress);
                h.Add(b.ConstructionTicks);
                h.Add(b.Cooldown);
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
                h.Add(u.MaxHp);
                h.Add((int)u.AttackKind);
                h.Add(u.AttackId);
                h.Add(u.Cooldown);
                if (u.KitCarry != 0) h.Add(u.KitCarry);
                h.Add(u.Marching ? 1 : 0);
                h.Add(u.Facing);
            }
            return h.Value;
        }

        /// <summary>Slot offsets for <paramref name="count"/> units, centred on (0,0), laid out in a grid.</summary>
        public static Vec2[] FormationOffsets(int count) => FormationOffsets(count, FormationSpacing);

        public static Vec2[] FormationOffsets(int count, Fix64 spacing)
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
                Fix64 x = spacing * Fix64.FromInt(col * 2 - (columns - 1)) * Fix64.Half;
                Fix64 y = spacing * Fix64.FromInt(row * 2 - (rows - 1)) * Fix64.Half;
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
                    case CancelBuildCommand cancelBuild: ApplyCancelBuild(cancelBuild); break;
                    case UpgradeBaseCommand upgrade: ApplyUpgradeBase(upgrade); break;
                    case ResearchCommand research: ApplyResearch(research); break;
                }
            }
            _pending.Clear();
        }

        private void ApplyMove(MoveCommand cmd)
        {
            var units = new List<Unit>(cmd.UnitIds.Length);
            Fix64 spacing = FormationSpacing;
            foreach (int id in cmd.UnitIds)
            {
                Unit u = GetUnit(id);
                if (u == null || u.Team != cmd.Team) continue;
                units.Add(u);
                Fix64 wide = u.Radius * Fix64.FromInt(2) + FormationGap; // heavy units need roomier slots
                if (wide > spacing) spacing = wide;
            }
            if (units.Count == 0) return;

            Fix64 cap = SlowestStep(units, cmd.Team);
            Vec2[] offsets = AssignSlots(units, FormationOffsets(units.Count, spacing), cmd.Target);
            for (int i = 0; i < units.Count; i++)
            {
                ClearTarget(units[i]); // a move order cancels an attack order
                units[i].SpeedCap = cap;
                GoTo(units[i], ClampToMap(cmd.Target + offsets[i]));
                units[i].Marching = units[i].Moving;
            }
        }

        /// <summary>The slowest step among the player's units in the list: a group travels together at that pace.</summary>
        private static Fix64 SlowestStep(List<Unit> units, int team)
        {
            Fix64 slowest = default;
            bool any = false;
            foreach (Unit u in units)
            {
                if (u.Team != team) continue;
                if (!any || u.StepPerTick < slowest) { slowest = u.StepPerTick; any = true; }
            }
            return slowest;
        }

        private void ApplyAttack(AttackCommand cmd)
        {
            if (!TargetPosition(cmd.TargetKind, cmd.TargetId, 1 - cmd.Team, out _)) return;

            var units = new List<Unit>(cmd.UnitIds.Length);
            foreach (int id in cmd.UnitIds)
            {
                Unit found = GetUnit(id);
                if (found != null) units.Add(found);
            }
            Fix64 cap = SlowestStep(units, cmd.Team);

            foreach (Unit u in units)
            {
                if (u.Team != cmd.Team) continue;
                u.SpeedCap = cap;
                u.Marching = false;
                u.AttackKind = cmd.TargetKind;
                u.AttackId = cmd.TargetId;
                u.Ordered = true;
            }
        }

        private void ApplyBuild(BuildCommand cmd)
        {
            if (!ValidTeam(cmd.Team) || CheckBuild(cmd.Kind, cmd.X, cmd.Y, cmd.Team) != BuildCheck.Ok) return;

            _players[cmd.Team].Resources -= Defs.Building(cmd.Kind).Cost;
            PlaceBuilding(cmd.Kind, cmd.X, cmd.Y, cmd.Team, underConstruction: true);

            // The new building may block routes that were planned before it existed.
            foreach (Unit u in _units)
                if (u.Moving) GoTo(u, u.Target);
        }

        /// <summary>Why a unit can or cannot be queued at a building.</summary>
        public QueueCheck CheckQueue(Building b, UnitKind kind)
        {
            if (b == null || b.UnderConstruction || Defs.Unit(kind).Producer != b.Kind) return QueueCheck.NotAllowed;
            if (b.QueueList.Count >= MaxQueue) return QueueCheck.QueueFull;
            PlayerState p = _players[b.Team];
            if (p.BaseLevel < Defs.Unit(kind).RequiredLevel) return QueueCheck.LevelTooLow;
            if (p.PopUsed + Defs.Unit(kind).Pop > p.PopCap) return QueueCheck.PopCapReached;
            if (p.Resources < Defs.Unit(kind).Cost) return QueueCheck.NotEnoughResources;
            return QueueCheck.Ok;
        }

        private void ApplyQueue(QueueUnitCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (b == null || b.Team != cmd.Team || CheckQueue(b, cmd.Kind) != QueueCheck.Ok) return;
            _players[b.Team].Resources -= Defs.Unit(cmd.Kind).Cost;
            b.QueueList.Add(cmd.Kind);
            RecountPop(b.Team);
        }

        private void ApplyCancel(CancelQueuedCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (b == null || b.Team != cmd.Team || cmd.Index < 0 || cmd.Index >= b.QueueList.Count) return;
            _players[b.Team].Resources += Defs.Unit(b.QueueList[cmd.Index]).Cost;
            b.QueueList.RemoveAt(cmd.Index);
            if (cmd.Index == 0) b.Progress = 0;
            RecountPop(b.Team);
        }

        private void ApplyCancelBuild(CancelBuildCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (b == null || b.Team != cmd.Team || !b.UnderConstruction || b.Hp <= 0) return;
            _players[b.Team].Resources += Defs.Building(b.Kind).Cost * Economy.CancelRefundPercent / 100;
            b.Hp = 0; // removed (and its cells freed) at the end of this tick, like a destroyed building
        }

        /// <summary>Why the base can or cannot be raised to its next level now.</summary>
        public UpgradeCheck CheckUpgradeBase(int team)
        {
            if (!ValidTeam(team) || !HasBuilding(BuildingKind.Hq, team)) return UpgradeCheck.NotAllowed;
            PlayerState p = _players[team];
            if (p.BaseLevel >= Economy.MaxBaseLevel) return UpgradeCheck.MaxLevel;
            if (p.Upgrading) return UpgradeCheck.AlreadyUpgrading;
            if (p.Resources < Economy.BaseUpgradeCost(p.BaseLevel)) return UpgradeCheck.NotEnoughResources;
            return UpgradeCheck.Ok;
        }

        public ResearchCheck CheckResearch(int team, UpgradeKind kind)
        {
            if (!ValidTeam(team) || !HasBuilding(BuildingKind.Hq, team)) return ResearchCheck.NotAllowed;
            PlayerState p = _players[team];
            UpgradeDef def = Defs.Upgrade(kind);
            int tier = p.UpgradeTiers[(int)kind];
            if (tier >= def.Tiers) return ResearchCheck.MaxTier;
            if (p.Researching) return ResearchCheck.AlreadyResearching;
            if (p.BaseLevel < def.RequiredLevel[tier]) return ResearchCheck.LevelTooLow;
            if (p.Resources < def.Cost[tier]) return ResearchCheck.NotEnoughResources;
            return ResearchCheck.Ok;
        }

        private void ApplyResearch(ResearchCommand cmd)
        {
            if (CheckResearch(cmd.Team, cmd.Kind) != ResearchCheck.Ok) return;
            PlayerState p = _players[cmd.Team];
            int tier = p.UpgradeTiers[(int)cmd.Kind];
            UpgradeDef def = Defs.Upgrade(cmd.Kind);
            p.Resources -= def.Cost[tier];
            p.ResearchKind = cmd.Kind;
            p.ResearchTicksLeft = def.Seconds[tier] * TicksPerSecond;
        }

        /// <summary>Total bonus (thousandths) a team has for this kind of unit and stat.</summary>
        public int UpgradeBonus(int team, UnitClass unitClass, UpgradeStat stat)
        {
            int bonus = 0;
            PlayerState p = _players[team];
            for (int k = 0; k < p.UpgradeTiers.Length; k++)
            {
                UpgradeDef def = Defs.Upgrade((UpgradeKind)k);
                if (def.Class == unitClass && def.Stat == stat) bonus += def.BonusMilli * p.UpgradeTiers[k];
            }
            return bonus;
        }

        /// <summary>Health of a new unit of this kind for this team: the base value plus the finished health upgrades.</summary>
        public int EffectiveMaxHp(int team, UnitKind kind) =>
            Defs.Combat(kind).Hp * (1000 + UpgradeBonus(team, Defs.Unit(kind).Class, UpgradeStat.Health) + _players[team].KitBonusMilli[(int)kind]) / 1000;

        /// <summary>Damage per shot of this kind of unit for this team.</summary>
        public int EffectiveDamage(int team, UnitKind kind) =>
            Defs.Combat(kind).Damage * (1000 + UpgradeBonus(team, Defs.Unit(kind).Class, UpgradeStat.Damage)) / 1000;

        /// <summary>
        /// Gives a team a permanent bonus (thousandths) to the damage and health of one kind of unit, e.g. from the player's armory (damage: see <see cref="Fire"/>).
        /// Meant for the start of a match: units that already exist get the extra health too. Both sides of an online match must set the same values.
        /// </summary>
        public void SetKitBonus(int team, UnitKind kind, int bonusMilli)
        {
            if (!ValidTeam(team)) return;
            _players[team].KitBonusMilli[(int)kind] = System.Math.Max(0, System.Math.Min(1000, bonusMilli));
            foreach (Unit u in _units)
            {
                if (u.Team != team || u.Kind != kind || u.Hp <= 0) continue;
                int max = EffectiveMaxHp(team, kind);
                u.Hp += max - u.MaxHp;
                u.MaxHp = max;
            }
        }

        /// <summary>When a health tier finishes, units that are already alive get the extra health too.</summary>
        private void ApplyHealthBonus(int team, UpgradeKind kind)
        {
            UpgradeDef def = Defs.Upgrade(kind);
            if (def.Stat != UpgradeStat.Health) return;
            foreach (Unit u in _units)
            {
                if (u.Team != team || Defs.Unit(u.Kind).Class != def.Class || u.Hp <= 0) continue;
                int max = EffectiveMaxHp(team, u.Kind);
                u.Hp += max - u.MaxHp;
                u.MaxHp = max;
            }
        }

        private void ApplyUpgradeBase(UpgradeBaseCommand cmd)
        {
            if (CheckUpgradeBase(cmd.Team) != UpgradeCheck.Ok) return;
            PlayerState p = _players[cmd.Team];
            p.Resources -= Economy.BaseUpgradeCost(p.BaseLevel);
            p.UpgradeTicksLeft = Economy.BaseUpgradeSeconds(p.BaseLevel) * TicksPerSecond;
        }

        /// <summary>Advances construction of new buildings and base upgrades by one tick.</summary>
        private void Construct()
        {
            foreach (Building b in _buildings)
            {
                if (!b.UnderConstruction || b.Hp <= 0) continue;
                // Health grows with the work done: from a tenth to full over the construction time (whole numbers only).
                int start = System.Math.Max(1, b.MaxHp / 10);
                int remaining = b.MaxHp - start;
                b.Hp += remaining * (b.BuildProgress + 1) / b.ConstructionTicks - remaining * b.BuildProgress / b.ConstructionTicks;
                b.BuildProgress++;
                if (!b.UnderConstruction) UpdateEconomy(b.Team); // now it needs energy and may give some
            }
            foreach (PlayerState p in _players)
            {
                if (p.UpgradeTicksLeft == 0) continue;
                if (--p.UpgradeTicksLeft == 0) p.BaseLevel++;
            }
            for (int team = 0; team < TeamCount; team++)
            {
                PlayerState p = _players[team];
                if (p.ResearchTicksLeft == 0) continue;
                if (--p.ResearchTicksLeft == 0)
                {
                    p.UpgradeTiers[(int)p.ResearchKind]++;
                    ApplyHealthBonus(team, p.ResearchKind);
                }
            }
        }

        /// <summary>Places taken by a team's living units and by the units waiting in its production queues.</summary>
        private void RecountPop(int team)
        {
            int used = 0;
            foreach (Unit u in _units)
                if (u.Team == team && u.Hp > 0) used += Defs.Unit(u.Kind).Pop;
            foreach (Building b in _buildings)
                if (b.Team == team)
                    foreach (UnitKind q in b.QueueList) used += Defs.Unit(q).Pop;
            _players[team].PopUsed = used;
        }

        private void ApplyRally(SetRallyCommand cmd)
        {
            Building b = GetBuilding(cmd.BuildingId);
            if (b == null || b.Team != cmd.Team || Defs.Produces(b.Kind).Length == 0) return;
            b.HasRally = true;
            b.Rally = ClampToMap(cmd.Point);
        }

        /// <summary>Advances every working production building; finished units appear at its door.</summary>
        private void ProduceUnits()
        {
            foreach (Building b in _buildings)
            {
                if (b.QueueList.Count == 0 || !b.Powered || b.UnderConstruction) continue;

                UnitKind kind = b.QueueList[0];
                if (b.Progress < Defs.Unit(kind).BuildSeconds * TicksPerSecond)
                {
                    b.Progress++;
                    continue;
                }

                // Door is on the side facing the middle of the map: the low-y side for team 0, and the half-turn image of that for team 1.
                var door = b.Team == 0
                    ? Pathfinder.NearestWalkable(Map, b.X + b.Width / 2, b.Y - 1)
                    : Pathfinder.NearestWalkable(Map, b.X + (b.Width - 1) / 2, b.Y + b.Height, mirrored: true);
                if (door == null) continue; // nowhere to stand: wait

                // Spread new units a little so they do not all sit on one point.
                Fix64 offset = Fix64.FromFraction((b.Spawned % 4) * 3 - 4, 10);
                if (b.Team == 1) offset = -offset; // half-turn image of team 0's spread
                var pos = new Vec2(Fix64.FromInt(door.Value.x) + Fix64.Half + offset, Fix64.FromInt(door.Value.y) + Fix64.Half);
                b.Spawned++;
                b.QueueList.RemoveAt(0);
                b.Progress = 0;

                Unit unit = SpawnUnit(kind, pos, b.Team);
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

        /// <summary>
        /// Shots fired this tick land together once everybody has fired, so the order units happen to be stored in
        /// never decides who gets the first strike (otherwise the side with the lower unit ids would always win mirrored fights).
        /// </summary>
        private void ApplyIncomingDamage()
        {
            foreach (Unit u in _units) { u.Hp -= u.Incoming; u.Incoming = 0; }
            foreach (Building b in _buildings) { b.Hp -= b.Incoming; b.Incoming = 0; }
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

        /// <summary>Towers shoot the nearest enemy unit in range; repair stations heal friendly units nearby. Both need energy and must be finished.</summary>
        private void DefenceAndRepair()
        {
            foreach (Building b in _buildings)
            {
                if (b.Hp <= 0 || b.UnderConstruction || !b.Powered) continue;
                BuildingDef def = Defs.Building(b.Kind);
                if (def.Damage == 0 && def.RepairPerSecond == 0) continue;

                var centre = new Vec2(Fix64.FromInt(b.X) + Fix64.FromInt(b.Width) * Fix64.Half, Fix64.FromInt(b.Y) + Fix64.FromInt(b.Height) * Fix64.Half);

                if (def.Damage > 0)
                {
                    if (b.Cooldown > 0) { b.Cooldown--; }
                    else if ((Tick + b.Phase) % 3 == 0) TowerFire(b, def, centre);
                }

                if (def.RepairPerSecond > 0 && (Tick + b.Phase) % TicksPerSecond == 0)
                {
                    Fix64 r2 = def.RepairRadius * def.RepairRadius;
                    foreach (Unit u in _units)
                    {
                        if (u.Team != b.Team || u.Hp <= 0 || u.Hp >= u.MaxHp) continue;
                        if ((u.Pos - centre).SqrLength <= r2) u.Hp = System.Math.Min(u.MaxHp, u.Hp + def.RepairPerSecond);
                    }
                }
            }
        }

        private void TowerFire(Building b, BuildingDef def, Vec2 centre)
        {
            Fix64 best = def.Range * def.Range + Fix64.One;
            Unit target = null;
            foreach (Unit t in _units)
            {
                if (t.Team == b.Team || t.Hp <= 0) continue;
                Fix64 d = (t.Pos - centre).SqrLength;
                if (d <= def.Range * def.Range && d < best) { best = d; target = t; }
            }
            if (target == null) return;

            b.Cooldown = def.CooldownTicks;
            target.Incoming += def.Damage;
            _shots.Add(new ShotEvent(centre, target.Pos, UnitKind.Rifleman, b.Team));
            if (_shots.Count > 512) _shots.RemoveRange(0, 256);
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
            if (u.AttackKind == AttackKind.Building && (Tick + u.Phase) % 5 == 0 && EnemyUnitInReach(u, def, enemyTeam))
            {
                ClearTarget(u);
                Stop(u); // stand and fight: the next step picks the nearest enemy unit
            }

            // Look for something to shoot: units that stand still (and computer guards, even on patrol) notice enemies.
            if (u.AttackKind == AttackKind.None && (!u.Moving || u.Guard) && (Tick + u.Phase) % 5 == 0)
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
            if ((Tick + u.Phase) % 10 == 0) GoTo(u, targetPos); // re-plan twice a second at most
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

            if (u.AttackKind == AttackKind.None && (Tick + u.Phase) % 3 == 0)
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

            int damage = EffectiveDamage(u.Team, u.Kind);
            if (u.AttackKind == AttackKind.Unit)
            {
                Unit target = GetUnit(u.AttackId);
                int mult = Defs.Unit(target.Kind).Class == UnitClass.Vehicle ? def.VsVehicleMilli : def.VsInfantryMilli;
                target.Incoming += WithKit(u, Scaled(damage, mult));
            }
            else GetBuilding(u.AttackId).Incoming += WithKit(u, Scaled(damage, def.VsBuildingMilli));
        }

        /// <summary>
        /// The armory's damage bonus on top of what a shot deals. A shot is only a few points (a rifleman against a building deals 1),
        /// so the bonus is carried from shot to shot: after many shots it adds exactly the bonus fraction.
        /// </summary>
        private int WithKit(Unit u, int dealt)
        {
            int kit = _players[u.Team].KitBonusMilli[(int)u.Kind];
            if (kit == 0 || dealt == 0) return dealt;
            u.KitCarry += dealt * kit;
            dealt += u.KitCarry / 1000;
            u.KitCarry %= 1000;
            return dealt;
        }

        private static int Scaled(int damage, int milli) => milli <= 0 ? 0 : System.Math.Max(1, damage * milli / 1000);

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
            if (buildingLost)
                for (int t = 0; t < TeamCount; t++) UpdateEconomy(t);

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
            foreach (PlayerState p in _players)
            {
                p.IncomeProgress += p.IncomePerMinute;
                while (p.IncomeProgress >= TicksPerMinute)
                {
                    p.IncomeProgress -= TicksPerMinute;
                    p.Resources++;
                }
            }
        }

        /// <summary>The cell a coordinate lies in. On an exact cell edge the mirrored side picks the lower cell (the half-turn image of the upper one).</summary>
        private static int CellOf(Fix64 v, bool mirrored) => System.Math.Max(0, (int)((mirrored ? v.Raw - 1 : v.Raw) >> Fix64.FractionBits));

        private static bool ValidTeam(int team) => team >= 0 && team < TeamCount;

        /// <summary>Plans a route for the unit. Blocked destinations are moved to the nearest free cell; unreachable ones are ignored.</summary>
        private void GoTo(Unit u, Vec2 target)
        {
            bool mirrored = u.Team == 1;
            // A point exactly on a cell edge belongs to the later cell for team 0 and to the earlier one for team 1, so both sides see mirror-image cells.
            int gx = CellOf(target.X, mirrored), gy = CellOf(target.Y, mirrored);

            // A blocked goal moves to the closest free cell (cheap). If that one cannot be reached either
            // (a closed-off pocket), the path ends on the reachable cell closest to it: this searches the whole map, so it is the last resort.
            var free = Pathfinder.NearestWalkable(Map, gx, gy, mirrored);
            if (free == null) { Stop(u); return; }
            gx = free.Value.x;
            gy = free.Value.y;

            int sx = CellOf(u.Pos.X, mirrored), sy = CellOf(u.Pos.Y, mirrored);
            var cells = Pathfinder.FindPath(Map, sx, sy, gx, gy, false, mirrored) ?? Pathfinder.FindPath(Map, sx, sy, gx, gy, true, mirrored);
            if (cells == null) { Stop(u); return; }

            var last = cells[cells.Count - 1];
            if (last.x != CellOf(target.X, mirrored) || last.y != CellOf(target.Y, mirrored))
                target = new Vec2(Fix64.FromInt(last.x) + Fix64.Half, Fix64.FromInt(last.y) + Fix64.Half);

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

        /// <summary>An intermediate waypoint counts as reached this close: units that are pushed around must not circle a point they cannot touch.</summary>
        private static readonly Fix64 WaypointReach = Fix64.FromFraction(3, 4);

        private void MoveUnit(Unit u)
        {
            if (!u.Moving) return;

            Vec2 waypoint = u.Path[u.PathIndex];
            bool last = u.PathIndex == u.Path.Length - 1;
            Vec2 delta = waypoint - u.Pos;
            Fix64 step = u.StepPerTick;
            if (u.SpeedCap.Raw > 0 && u.SpeedCap < step) step = u.SpeedCap;

            Fix64 reach = last || WaypointReach < step ? step : WaypointReach;
            if (delta.SqrLength <= reach * reach)
            {
                if (last)
                {
                    u.Pos = waypoint;
                    Stop(u);
                }
                else
                {
                    u.PathIndex++; // the next tick heads for the next waypoint
                }
                return;
            }

            Vec2 dir = delta.Normalized();
            // The straight line to the waypoint can clip a rock corner when the unit was pushed off its path:
            // slide along the wall, and re-plan if even that is blocked.
            if (!TryShift(u, dir * step)) GoTo(u, u.Target);
            u.Facing = dir;
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
