using System.Collections.Generic;

namespace SimCore
{
    public enum AiDifficulty { Easy, Medium, Hard }

    /// <summary>
    /// A computer opponent. It plays like a person: every decision is an ordinary <see cref="Command"/> that the
    /// host enqueues for the AI's team, so it works with replays and never "cheats" with resources.
    /// It sees what a player would see (enemy units only inside the sight of its own units; enemy buildings once
    /// spotted; the enemy headquarters from the start, as both start spots are known on a 1v1 map).
    /// The state is only its own counters and its own <see cref="DeterministicRandom"/>, so the same seed gives the same game.
    /// </summary>
    public sealed class AiPlayer
    {
        public int Team { get; }
        public AiDifficulty Difficulty { get; }

        private readonly DeterministicRandom _rng;
        private readonly int _interval;      // ticks between two decisions
        private int _wave;                    // attack waves sent so far
        private bool _attacking;
        private readonly HashSet<int> _knownEnemyBuildings = new HashSet<int>();

        public AiPlayer(int team, AiDifficulty difficulty, ulong seed)
        {
            Team = team;
            Difficulty = difficulty;
            _rng = new DeterministicRandom(seed ^ ((ulong)team * 0x9E3779B97F4A7C15UL) ^ 0xA1);
            _interval = difficulty == AiDifficulty.Easy ? 60 : difficulty == AiDifficulty.Medium ? 30 : 10;
        }

        /// <summary>Call once per tick, before <see cref="World.Step"/>. Commands (if any) are added to <paramref name="commands"/>; enqueue them for <see cref="Team"/>.</summary>
        public void Update(World w, List<Command> commands)
        {
            if (w.Tick % _interval != 0) return;
            if (!w.HasBuilding(BuildingKind.Hq, Team)) return;
            // Mistakes: a distracted turn does nothing at all.
            int distracted = Difficulty == AiDifficulty.Easy ? 35 : Difficulty == AiDifficulty.Medium ? 15 : 0;
            if (distracted > 0 && _rng.Next(100) < distracted) return;

            var ctx = new Context(w, Team);
            if (ctx.Hq == null) return;
            RememberEnemyBuildings(ctx);

            PlanBuildings(ctx, commands);
            PlanProduction(ctx, commands);
            PlanBaseUpgrade(ctx, commands);
            PlanResearch(ctx, commands);
            PlanArmy(ctx, commands);
        }

        // ------------------------------------------------------------------------------------ what the AI knows

        private sealed class Context
        {
            public readonly World W;
            public readonly int Team;
            public readonly PlayerState Me;
            public readonly Building Hq;
            public readonly List<Building> Mine = new List<Building>();
            public readonly List<Unit> Army = new List<Unit>();
            public readonly List<Unit> VisibleEnemies = new List<Unit>();
            public readonly int[] Count = new int[System.Enum.GetValues(typeof(BuildingKind)).Length]; // own buildings incl. under construction
            public int UnderConstruction;
            public int Budget;      // resources not yet promised to an order given in this round
            public int PopFree;
            public Vec2 EnemyHq;
            public bool KnowsEnemyHq;
            public Vec2 Staging;

            public Context(World w, int team)
            {
                W = w;
                Team = team;
                Me = w.Player(team);
                Budget = Me.Resources;
                PopFree = Me.PopCap - Me.PopUsed;

                foreach (Building b in w.Buildings)
                {
                    if (b.Hp <= 0) continue;
                    if (b.Team == team)
                    {
                        Mine.Add(b);
                        Count[(int)b.Kind]++;
                        if (b.UnderConstruction) UnderConstruction++;
                        if (b.Kind == BuildingKind.Hq) Hq = b;
                    }
                    else if (b.Kind == BuildingKind.Hq)
                    {
                        EnemyHq = Center(b);
                        KnowsEnemyHq = true;
                    }
                }
                foreach (Unit u in w.Units) if (u.Team == team && u.Hp > 0) Army.Add(u);

                // Enemy units count only when one of our units (or buildings) could see them.
                foreach (Unit e in w.Units)
                {
                    if (e.Team == team || e.Hp <= 0) continue;
                    if (Seen(e.Pos)) VisibleEnemies.Add(e);
                }

                if (Hq != null)
                {
                    Vec2 home = Center(Hq);
                    Vec2 dir = KnowsEnemyHq ? (EnemyHq - home).Normalized() : new Vec2(Fix64.Zero, Fix64.One);
                    Staging = home + dir * Fix64.FromInt(11);
                }
            }

            private bool Seen(Vec2 p)
            {
                foreach (Unit u in Army)
                {
                    Fix64 s = Defs.Combat(u.Kind).Sight;
                    if ((u.Pos - p).SqrLength <= s * s) return true;
                }
                Fix64 base2 = Fix64.FromInt(14 * 14);
                foreach (Building b in Mine) if ((Center(b) - p).SqrLength <= base2) return true;
                return false;
            }
        }

        private static Vec2 Center(Building b) =>
            new Vec2(Fix64.FromInt(b.X) + Fix64.FromInt(b.Width) * Fix64.Half, Fix64.FromInt(b.Y) + Fix64.FromInt(b.Height) * Fix64.Half);

        private void RememberEnemyBuildings(Context c)
        {
            foreach (Building b in c.W.Buildings)
            {
                if (b.Team == Team || b.Hp <= 0 || _knownEnemyBuildings.Contains(b.Id)) continue;
                Vec2 centre = Center(b);
                bool seen = false;
                foreach (Unit u in c.Army)
                {
                    Fix64 s = Defs.Combat(u.Kind).Sight;
                    if ((u.Pos - centre).SqrLength <= s * s) { seen = true; break; }
                }
                if (seen) _knownEnemyBuildings.Add(b.Id);
            }
        }

        // ------------------------------------------------------------------------------------ buildings

        private int Target(int easy, int medium, int hard) =>
            Difficulty == AiDifficulty.Easy ? easy : Difficulty == AiDifficulty.Medium ? medium : hard;

        private void PlanBuildings(Context c, List<Command> output)
        {
            int inFlight = 1; // building two things at once starves the army of money
            if (c.UnderConstruction >= inFlight) return;

            int energyFree = c.Me.EnergyCapacity - c.Me.EnergyUsed;
            foreach (Building b in c.Mine) if (b.UnderConstruction) energyFree -= Defs.Building(b.Kind).EnergyUse;

            BuildingKind? want = null;
            int supply = c.Count[(int)BuildingKind.SupplyCenter], barracks = c.Count[(int)BuildingKind.Barracks], factories = c.Count[(int)BuildingKind.VehicleFactory];
            if (c.Count[(int)BuildingKind.PowerPlant] == 0) want = BuildingKind.PowerPlant;
            else if (barracks == 0) want = BuildingKind.Barracks;
            else if (supply < 1) want = BuildingKind.SupplyCenter;
            else if (Difficulty == AiDifficulty.Easy) want = factories == 0 ? BuildingKind.VehicleFactory : (BuildingKind?)null; // an easy opponent stops expanding early
            else if (supply < 2) want = BuildingKind.SupplyCenter;
            else if (factories == 0) want = BuildingKind.VehicleFactory;
            else if (supply < Target(1, 2, 4)) want = BuildingKind.SupplyCenter;
            else if (barracks < 2) want = BuildingKind.Barracks;
            else if (c.Me.BaseLevel >= 2 && c.Count[(int)BuildingKind.RepairStation] == 0) want = BuildingKind.RepairStation;
            else if (Difficulty == AiDifficulty.Hard && c.Me.BaseLevel >= 2 && c.Count[(int)BuildingKind.Tower] < 2) want = BuildingKind.Tower;
            else if (Difficulty == AiDifficulty.Hard && factories < 2) want = BuildingKind.VehicleFactory;
            if (want == null) return;

            // Not enough energy for it: a power plant comes first.
            BuildingKind kind = want.Value;
            if (Defs.Building(kind).EnergyUse > energyFree && kind != BuildingKind.PowerPlant) kind = BuildingKind.PowerPlant;

            BuildingDef def = Defs.Building(kind);
            if (c.Budget < def.Cost)
            {
                // Medium and Hard save for the building instead of spending everything on units first (the units
                // would never let the money reach a supply center, and the economy would stay at its start).
                if (Difficulty != AiDifficulty.Easy && c.Me.BaseLevel >= def.RequiredLevel) c.Budget -= def.Cost;
                return;
            }
            if (c.Me.BaseLevel < def.RequiredLevel) return;

            var spot = FindSpot(c, kind);
            if (spot == null) return;
            output.Add(new BuildCommand(kind, spot.Value.x, spot.Value.y));
            c.Budget -= def.Cost;
        }

        /// <summary>The first free spot around the headquarters, nearest rings first, leaving a one-cell lane between buildings.</summary>
        private (int x, int y)? FindSpot(Context c, BuildingKind kind)
        {
            BuildingDef def = Defs.Building(kind);
            int cx = c.Hq.X + c.Hq.Width / 2, cy = c.Hq.Y + c.Hq.Height / 2;
            for (int r = 4; r <= 18; r++)
            {
                // The scan order is mirrored for the second team, so that both sides fill their base the same way
                // (otherwise one side always builds towards the enemy and the other towards the map edge).
                for (int iy = -r; iy <= r; iy++)
                {
                    int dy = Team == 0 ? iy : -iy;
                    for (int ix = -r; ix <= r; ix++)
                    {
                        int dx = Team == 0 ? ix : -ix;
                        if ((dx < 0 ? -dx : dx) != r && (dy < 0 ? -dy : dy) != r) continue;
                        int x = cx + dx - def.Width / 2, y = cy + dy - def.Height / 2;
                        if (!c.W.Map.InBounds(x, y) || !c.W.Map.InBounds(x + def.Width - 1, y + def.Height - 1)) continue;
                        if (!HasLane(c.W.Map, x, y, def.Width, def.Height)) continue;
                        if (c.W.CheckBuild(kind, x, y, c.Team) == BuildCheck.Ok) return (x, y);
                    }
                }
            }
            return null;
        }

        private static bool HasLane(MapGrid map, int x, int y, int w, int h)
        {
            for (int cy = y - 1; cy <= y + h; cy++)
                for (int cx = x - 1; cx <= x + w; cx++)
                    if (map.InBounds(cx, cy) && map.BuildingAt(cx, cy) != 0) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------ units

        private void PlanProduction(Context c, List<Command> output)
        {
            int queueLimit = Target(1, 2, 3);
            int enemyVehicles = 0, enemyInfantry = 0;
            foreach (Unit e in c.VisibleEnemies)
            {
                if (Defs.Unit(e.Kind).Class == UnitClass.Vehicle) enemyVehicles++; else enemyInfantry++;
            }

            // Cheap, good-value infantry comes first; the money for a full barracks queue is set aside before
            // anything expensive (a tank) is bought, so a early tank does not starve the army.
            Building[] order = c.Mine.ToArray();
            System.Array.Sort(order, (x, y) =>
            {
                int cx = x.Kind == BuildingKind.Barracks ? 0 : 1, cy = y.Kind == BuildingKind.Barracks ? 0 : 1;
                return cx != cy ? cx.CompareTo(cy) : x.Id.CompareTo(y.Id);
            });

            int reserved = 0; // money kept back for the barracks' next units
            foreach (Building b in order)
            {
                UnitKind[] kinds = Defs.Produces(b.Kind);
                if (kinds.Length == 0 || b.UnderConstruction) continue;

                if (!b.HasRally) output.Add(new SetRallyCommand(b.Id, c.Staging));
                bool isBarracks = b.Kind == BuildingKind.Barracks;
                int room = queueLimit - b.Queue.Count;
                if (room <= 0) continue;
                if (isBarracks && InfantryFull(c)) continue; // the rest of the unit limit is kept for vehicles

                int savedBudget = c.Budget;
                if (!isBarracks) c.Budget -= reserved; // vehicles only from what the infantry does not need
                UnitKind? choice = ChooseUnit(c, b, kinds, enemyVehicles, enemyInfantry);
                if (!isBarracks) c.Budget = savedBudget;

                if (choice == null)
                {
                    if (isBarracks) reserved += Defs.Unit(Defs.Produces(b.Kind)[0]).Cost * room; // saving up for the first kind
                    continue;
                }
                UnitDef ud = Defs.Unit(choice.Value);
                output.Add(new QueueUnitCommand(b.Id, choice.Value));
                c.Budget -= ud.Cost;
                c.PopFree -= ud.Pop;
                if (isBarracks) reserved += ud.Cost * (room - 1);
            }
        }

        /// <summary>
        /// Fighting value of a unit kind for the AI: health x damage per second, divided by what it costs
        /// (squared: two units of half the price beat one of full price). When the unit limit is nearly full the
        /// scarce thing is room, not money, so then it divides by the places the unit takes instead.
        /// </summary>
        private static long Value(UnitKind k, bool popPressure)
        {
            CombatDef cd = Defs.Combat(k);
            UnitDef ud = Defs.Unit(k);
            long strength = (long)cd.Hp * cd.Damage * World.TicksPerSecond * 100 / cd.CooldownTicks;
            long scarce = popPressure ? (long)ud.Pop * ud.Pop * 25 : (long)ud.Cost * ud.Cost / 25 + 1;
            return strength / System.Math.Max(1, scarce) + 1;
        }

        private static UnitKind? Affordable(Context c, UnitKind k) => c.Budget >= Defs.Unit(k).Cost && c.PopFree >= Defs.Unit(k).Pop ? k : (UnitKind?)null;

        /// <summary>
        /// A computer player that has a factory keeps part of its unit limit for vehicles, otherwise cheap infantry fills it
        /// and the factory never gets a place (Easy does not bother; Medium keeps 30 %, Hard 40 %).
        /// </summary>
        private bool InfantryFull(Context c)
        {
            int share = Target(100, 70, 100);
            if (share >= 100 || c.Count[(int)BuildingKind.VehicleFactory] == 0) return false;
            int infantry = 0;
            foreach (Unit u in c.Army) if (Defs.Unit(u.Kind).Class == UnitClass.Infantry) infantry += Defs.Unit(u.Kind).Pop;
            foreach (Building b in c.Mine)
                foreach (UnitKind q in b.Queue) if (Defs.Unit(q).Class == UnitClass.Infantry) infantry += Defs.Unit(q).Pop;
            return infantry >= c.Me.PopCap * share / 100;
        }

        /// <summary>How well a kind hurts what the enemy has (thousandths): its damage multipliers weighted by the enemy's mix of vehicles and infantry, plus a little for buildings.</summary>
        private static long Effectiveness(UnitKind k, int enemyVehicles, int enemyInfantry)
        {
            CombatDef cd = Defs.Combat(k);
            long inf = enemyInfantry + 1, veh = enemyVehicles + 1;
            long units = (inf * cd.VsInfantryMilli + veh * cd.VsVehicleMilli) / (inf + veh);
            return (units * 3 + cd.VsBuildingMilli) / 4;
        }

        private UnitKind? ChooseUnit(Context c, Building b, UnitKind[] kinds, int enemyVehicles, int enemyInfantry)
        {
            bool popPressure = c.Me.PopUsed * 100 >= c.Me.PopCap * 70;
            var options = new List<UnitKind>();
            var weights = new List<long>();
            foreach (UnitKind k in kinds)
            {
                UnitDef ud = Defs.Unit(k);
                if (ud.RequiredLevel > c.Me.BaseLevel) continue;
                // Factories wait (money, room) for the unit they want instead of buying the cheapest one.
                if (b.Kind == BuildingKind.Barracks && (c.Budget < ud.Cost || c.PopFree < ud.Pop)) continue;
                QueueCheck check = c.W.CheckQueue(b, k);
                bool onlyMoney = check == QueueCheck.NotEnoughResources || check == QueueCheck.PopCapReached;
                if (check != QueueCheck.Ok && !(onlyMoney && b.Kind != BuildingKind.Barracks)) continue;

                long weight = 100;
                if (Difficulty != AiDifficulty.Easy)
                {
                    weight = Value(k, popPressure) * Effectiveness(k, enemyVehicles, enemyInfantry) / 1000 + 1;
                }
                options.Add(k);
                weights.Add(weight);
            }
            if (options.Count == 0) return null;

            if (Difficulty == AiDifficulty.Hard)
            {
                int best = 0;
                for (int i = 1; i < options.Count; i++) if (weights[i] > weights[best]) best = i;
                return Affordable(c, options[best]);
            }
            long total = 0;
            foreach (long wgt in weights) total += wgt;
            long roll = (long)(_rng.NextUInt32() % (ulong)total);
            for (int i = 0; i < options.Count; i++)
            {
                if (roll < weights[i]) return Affordable(c, options[i]);
                roll -= weights[i];
            }
            return Affordable(c, options[options.Count - 1]);
        }

        // ------------------------------------------------------------------------------------ base level and research

        private void PlanBaseUpgrade(Context c, List<Command> output)
        {
            if (c.W.CheckUpgradeBase(Team) != UpgradeCheck.Ok) return;
            int cost = Economy.BaseUpgradeCost(c.Me.BaseLevel);
            int reserve = Target(150, 80, 40);
            if (c.Budget < cost + reserve) return;
            if (c.Count[(int)BuildingKind.Barracks] == 0 || c.Count[(int)BuildingKind.VehicleFactory] == 0) return;
            int popPercent = Target(90, 70, 50);
            if (c.Me.PopUsed * 100 < c.Me.PopCap * popPercent) return;
            output.Add(new UpgradeBaseCommand());
            c.Budget -= cost;
        }

        private void PlanResearch(Context c, List<Command> output)
        {
            if (Difficulty == AiDifficulty.Easy || c.Me.Researching) return;
            if (c.Army.Count < 4) return;

            int infantry = 0, vehicles = 0;
            foreach (Unit u in c.Army) if (Defs.Unit(u.Kind).Class == UnitClass.Infantry) infantry++; else vehicles++;
            bool infantryFirst = infantry >= vehicles;

            UpgradeKind[] order = infantryFirst
                ? new[] { UpgradeKind.InfantryDamage, UpgradeKind.InfantryHealth, UpgradeKind.VehicleDamage, UpgradeKind.VehicleHealth }
                : new[] { UpgradeKind.VehicleDamage, UpgradeKind.VehicleHealth, UpgradeKind.InfantryDamage, UpgradeKind.InfantryHealth };
            foreach (UpgradeKind k in order)
            {
                if (c.W.CheckResearch(Team, k) != ResearchCheck.Ok) continue;
                int cost = Defs.Upgrade(k).Cost[c.Me.UpgradeTier(k)];
                if (c.Budget < cost + 100) continue;
                output.Add(new ResearchCommand(k));
                c.Budget -= cost;
                return;
            }
        }

        // ------------------------------------------------------------------------------------ army

        private void PlanArmy(Context c, List<Command> output)
        {
            if (c.Army.Count == 0) { _attacking = false; return; }

            var fighters = new List<Unit>(c.Army);
            if (Difficulty == AiDifficulty.Hard) PullBackWounded(c, fighters, output);
            if (fighters.Count == 0) return;

            // 0. A hard opponent concentrates fire: everybody who can reach the weakest enemy nearby shoots at it.
            if (Difficulty == AiDifficulty.Hard && FocusFire(c, fighters, output)) { }

            // 1. Defend: an enemy near any of our buildings gets everyone's attention.
            Unit threat = NearestThreat(c);
            if (threat != null)
            {
                AttackUnit(fighters, threat, output);
                return;
            }

            // 2. Attack in waves once the army is big enough.
            int armyPop = 0;
            foreach (Unit u in fighters) armyPop += Defs.Unit(u.Kind).Pop;
            int threshold = Target(14, 12, 16) + _wave * Target(3, 2, 2);
            if (Difficulty != AiDifficulty.Easy)
            {
                // Do not march into a bigger army: wait for 25 % more than what we have seen of the enemy's.
                int enemyPop = 0;
                foreach (Unit e in c.VisibleEnemies) enemyPop += Defs.Unit(e.Kind).Pop;
                threshold = System.Math.Max(threshold, enemyPop * Target(5, 5, 3) / Target(4, 4, 2) + 2);
            }
            threshold = System.Math.Min(threshold, c.Me.PopCap * 8 / 10);

            // Nobody rushes in the first minutes: the economy and the first buildings come first.
            bool tooEarly = c.W.Tick < Target(4, 3, 2) * 60 * World.TicksPerSecond + 30 * World.TicksPerSecond;
            if (!_attacking && !tooEarly && armyPop >= threshold) { _attacking = true; _wave++; }
            if (_attacking && armyPop * 3 <= threshold) _attacking = false; // most of the wave is gone: regroup

            if (!_attacking)
            {
                var idle = new List<Unit>();
                foreach (Unit u in fighters)
                {
                    if (u.Moving || u.AttackKind != AttackKind.None) continue;
                    if ((u.Pos - c.Staging).SqrLength > Fix64.FromInt(36)) idle.Add(u);
                }
                if (idle.Count > 0) output.Add(new MoveCommand(Ids(idle), c.Staging));
                return;
            }

            // Fighting enemy units near the army come first, then a building.
            Vec2 centre = Centroid(fighters);
            Unit near = NearestVisible(c, centre, 9);
            if (near != null) { AttackUnit(fighters, near, output); return; }

            Building target = ChooseTargetBuilding(c, centre);
            if (target == null) return;
            var notYet = new List<Unit>();
            foreach (Unit u in fighters)
                if (u.AttackKind == AttackKind.None) notYet.Add(u); // units already in a fight keep it: they pick their own nearest enemy
            if (notYet.Count > 0) output.Add(new AttackCommand(Ids(notYet), AttackKind.Building, target.Id));
        }

        /// <summary>Orders every unit that can reach it to shoot the weakest enemy unit near the army.</summary>
        private bool FocusFire(Context c, List<Unit> fighters, List<Command> output)
        {
            Vec2 centre = Centroid(fighters);
            Unit weakest = null;
            Fix64 reach = Fix64.FromInt(10 * 10);
            foreach (Unit e in c.VisibleEnemies)
            {
                if ((e.Pos - centre).SqrLength > reach) continue;
                if (weakest == null || e.Hp < weakest.Hp || (e.Hp == weakest.Hp && e.Id < weakest.Id)) weakest = e;
            }
            if (weakest == null) return false;

            var shooters = new List<Unit>();
            foreach (Unit u in fighters)
            {
                if (u.AttackKind == AttackKind.Unit && u.AttackId == weakest.Id) continue;
                Fix64 range = Defs.Combat(u.Kind).Range + Fix64.One;
                if ((weakest.Pos - u.Pos).SqrLength <= range * range) shooters.Add(u);
            }
            if (shooters.Count > 0) output.Add(new AttackCommand(Ids(shooters), AttackKind.Unit, weakest.Id));
            return true;
        }

        private void PullBackWounded(Context c, List<Unit> fighters, List<Command> output)
        {
            // Pulling a unit out of the fight only pays when something there will heal it.
            Building station = null;
            foreach (Building b in c.Mine)
                if (b.Kind == BuildingKind.RepairStation && !b.UnderConstruction && b.Powered) { station = b; break; }
            if (station == null) return;
            Vec2 haven = Center(station);

            var hurt = new List<Unit>();
            foreach (Unit u in fighters)
                if (u.Hp * 100 < u.MaxHp * 30) hurt.Add(u);
            if (hurt.Count == 0) return;

            var go = new List<Unit>();
            foreach (Unit u in hurt)
            {
                fighters.Remove(u);
                if ((u.Pos - haven).SqrLength > Fix64.FromInt(16)) go.Add(u);
            }
            if (go.Count > 0) output.Add(new MoveCommand(Ids(go), haven));
        }

        private Unit NearestThreat(Context c)
        {
            Unit best = null;
            Fix64 bestD = Fix64.MaxValue;
            Fix64 reach = Fix64.FromInt(16 * 16);
            foreach (Unit e in c.VisibleEnemies)
            {
                foreach (Building b in c.Mine)
                {
                    Fix64 d = (e.Pos - Center(b)).SqrLength;
                    if (d <= reach && d < bestD) { bestD = d; best = e; }
                }
            }
            return best;
        }

        private static Unit NearestVisible(Context c, Vec2 from, int range)
        {
            Unit best = null;
            Fix64 bestD = Fix64.FromInt(range * range) + Fix64.One;
            foreach (Unit e in c.VisibleEnemies)
            {
                Fix64 d = (e.Pos - from).SqrLength;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private Building ChooseTargetBuilding(Context c, Vec2 centre)
        {
            Building hq = null, nearest = null;
            Fix64 nearestD = Fix64.MaxValue;
            foreach (Building b in c.W.Buildings)
            {
                if (b.Team == Team || b.Hp <= 0) continue;
                if (b.Kind == BuildingKind.Hq) hq = b;
                if (!_knownEnemyBuildings.Contains(b.Id)) continue;
                Fix64 d = (Center(b) - centre).SqrLength;
                if (d < nearestD) { nearestD = d; nearest = b; }
            }
            if (Difficulty == AiDifficulty.Easy) return hq ?? nearest;
            if (Difficulty == AiDifficulty.Medium) return nearest ?? hq;
            // Hard: clear what is close first (towers shoot back), then go for the headquarters.
            if (nearest != null && nearestD <= Fix64.FromInt(18 * 18)) return nearest;
            return hq ?? nearest;
        }

        private static void AttackUnit(List<Unit> fighters, Unit target, List<Command> output)
        {
            var notYet = new List<Unit>();
            foreach (Unit u in fighters)
                if (u.AttackKind == AttackKind.None) notYet.Add(u); // a unit already shooting someone keeps its own target
            if (notYet.Count > 0) output.Add(new AttackCommand(Ids(notYet), AttackKind.Unit, target.Id));
        }

        private static Vec2 Centroid(List<Unit> units)
        {
            Vec2 sum = Vec2.Zero;
            foreach (Unit u in units) sum = sum + u.Pos;
            Fix64 n = Fix64.FromInt(units.Count);
            return new Vec2(sum.X / n, sum.Y / n);
        }

        private static int[] Ids(List<Unit> units)
        {
            var ids = new int[units.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = units[i].Id;
            return ids;
        }
    }
}
