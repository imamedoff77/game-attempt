using System;
using System.Collections.Generic;
using System.Text;

namespace SimCore
{
    /// <summary>The ten missions of the campaign, easiest first, and the maps they are played on.</summary>
    public static class Campaign
    {
        public const int Size = 64;

        /// <summary>The mission that must be won to unlock online play (missions open one after the other, so this means the first three).</summary>
        public const int OnlineUnlockMission = 3;

        public static IReadOnlyList<Mission> Missions { get; } = Build();

        public static Mission Get(int number) => Missions[number - 1];

        private static IReadOnlyList<Mission> Build()
        {
            Objective Hq() => new Objective(ObjectiveKind.DestroyEnemyHq);
            Objective Survive(int seconds) => new Objective(ObjectiveKind.Survive, seconds);
            var none = new AttackWave[0];

            return new[]
            {
                // 1. A small outpost, no computer opponent: learn to train and attack.
                new Mission(1, seed => Outpost(), null, new[] { Hq() }, none, 0, 360, 3, 20),

                // 2. Economy: supply centers and an army, in peace.
                new Mission(2, seed => Peace(), null,
                    new[] { new Objective(ObjectiveKind.BuildBuildings, 2, BuildingKind.SupplyCenter), new Objective(ObjectiveKind.ArmyPop, 12) },
                    none, 0, 420, 0, 20),

                // 3. Hold the line against scripted waves.
                new Mission(3, seed => Defence(1, false), null, new[] { Survive(300) }, new[]
                {
                    new AttackWave(40, UnitKind.Rifleman, 4, 46, 46), new AttackWave(110, UnitKind.Rifleman, 6, 46, 46),
                    new AttackWave(180, UnitKind.Rifleman, 6, 46, 46), new AttackWave(180, UnitKind.RocketTrooper, 2, 50, 46),
                    new AttackWave(250, UnitKind.Rifleman, 8, 46, 46),
                }, 0, 310, 8, 25),

                // 4. First real match: an easy computer opponent with a weakened headquarters.
                new Mission(4, seed => Skirmish(seed, 6000, 0), AiDifficulty.Easy, new[] { Hq() }, none, 0, 600, 10, 30),

                // 5. Vehicles: wipe out a guarded base.
                new Mission(5, seed => Stronghold(), null, new[] { new Objective(ObjectiveKind.DestroyEnemyBuildings) }, none, 0, 720, 6, 30),

                // 6. A canyon with one way through: survive and thin out the enemy.
                new Mission(6, seed => Canyon(), AiDifficulty.Easy, new[] { Survive(360), new Objective(ObjectiveKind.KillEnemyUnits, 20) }, new[]
                {
                    new AttackWave(90, UnitKind.Jeep, 3, 50, 30), new AttackWave(200, UnitKind.Jeep, 3, 50, 30), new AttackWave(200, UnitKind.Rifleman, 4, 52, 34),
                }, 0, 400, 12, 35),

                // 7. A medium computer opponent.
                new Mission(7, seed => Skirmish(seed, 8000, 300), AiDifficulty.Medium, new[] { Hq() }, none, 0, 900, 15, 40),

                // 8. A fortress with towers.
                new Mission(8, seed => Fortress(), AiDifficulty.Medium, new[] { Hq() }, none, 0, 1000, 15, 45),

                // 9. Last stand: heavy waves, strong base.
                new Mission(9, seed => Defence(2, true), null, new[] { Survive(480) }, new[]
                {
                    new AttackWave(60, UnitKind.Rifleman, 6, 46, 46), new AttackWave(120, UnitKind.Jeep, 4, 46, 46),
                    new AttackWave(180, UnitKind.Tank, 2, 46, 46), new AttackWave(180, UnitKind.Rifleman, 6, 50, 46),
                    new AttackWave(260, UnitKind.RocketTrooper, 4, 46, 46), new AttackWave(260, UnitKind.Rifleman, 6, 50, 46),
                    new AttackWave(340, UnitKind.Tank, 3, 46, 46), new AttackWave(420, UnitKind.Tank, 4, 46, 46), new AttackWave(420, UnitKind.ArmoredCar, 2, 50, 46),
                }, 0, 490, 14, 50),

                // 10. The full match against a hard computer opponent.
                new Mission(10, seed => Skirmish(seed, 0, 0), AiDifficulty.Hard, new[] { Hq() }, none, 0, 1200, 25, 75),
            };
        }

        // ------------------------------------------------------------------------------------ maps

        private static MapGrid NewMap() => new MapGrid(Size, Size);

        private static void Fill(MapGrid map, int x, int y, int width, int height)
        {
            for (int cy = y; cy < y + height; cy++)
                for (int cx = x; cx < x + width; cx++)
                    map.SetTerrain(cx, cy, TerrainType.Rock);
        }

        private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

        private static void Troops(World w, UnitKind kind, int count, int x, int y, int team, bool guard)
        {
            for (int i = 0; i < count; i++)
            {
                Unit u = w.SpawnUnit(kind, P(x + i % 4, y + i / 4), team);
                if (guard) w.SetGuard(u);
            }
        }

        private static void ShrinkEnemyHq(World w, int hp)
        {
            if (hp <= 0) return;
            foreach (Building b in w.Buildings)
                if (b.Team == 1 && b.Kind == BuildingKind.Hq) { w.SetBuildingHealth(b.Id, hp); return; }
        }

        /// <summary>The player's base in the bottom-left corner.</summary>
        private static void PlayerBase(World w, bool factory, bool supply)
        {
            w.PlaceBuilding(BuildingKind.Hq, 8, 8, 0);
            w.PlaceBuilding(BuildingKind.PowerPlant, 16, 8, 0);
            w.PlaceBuilding(BuildingKind.Barracks, 8, 16, 0);
            if (factory) w.PlaceBuilding(BuildingKind.VehicleFactory, 16, 16, 0);
            if (supply) w.PlaceBuilding(BuildingKind.SupplyCenter, 20, 8, 0);
        }

        private static World Outpost()
        {
            var map = NewMap();
            Fill(map, 26, 24, 5, 2); Fill(map, 30, 34, 2, 5);
            var w = new World(map);
            PlayerBase(w, false, false);
            w.GiveResources(0, 150);
            Troops(w, UnitKind.Rifleman, 4, 12, 22, 0, false);
            w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
            ShrinkEnemyHq(w, 2000);
            Troops(w, UnitKind.Rifleman, 6, 45, 45, 1, true);
            return w;
        }

        private static World Peace()
        {
            var map = NewMap();
            Fill(map, 26, 24, 5, 2);
            var w = new World(map);
            PlayerBase(w, false, false);
            w.GiveResources(0, 150);
            Troops(w, UnitKind.Rifleman, 4, 12, 22, 0, false);
            w.PlaceBuilding(BuildingKind.Hq, 52, 52, 1);
            return w;
        }

        /// <summary>Player base only; the enemy sends scripted waves. <paramref name="level"/> 2 also gives a factory and tanks.</summary>
        private static World Defence(int level, bool strong)
        {
            var map = NewMap();
            Fill(map, 26, 24, 4, 2); Fill(map, 20, 30, 2, 4); Fill(map, 34, 20, 2, 5);
            var w = new World(map);
            PlayerBase(w, strong, true);
            w.SetBaseLevel(0, level);
            w.GiveResources(0, strong ? 900 : 200);
            Troops(w, UnitKind.Rifleman, 6, 12, 22, 0, false);
            if (strong) Troops(w, UnitKind.Tank, 2, 22, 24, 0, false);
            w.PlaceBuilding(BuildingKind.Hq, 52, 52, 1);
            return w;
        }

        private static World Skirmish(ulong seed, int enemyHqHealth, int extraResources)
        {
            World w = SkirmishScenario.Create(seed);
            ShrinkEnemyHq(w, enemyHqHealth);
            if (extraResources > 0) w.GiveResources(0, extraResources);
            return w;
        }

        private static World Stronghold()
        {
            var map = NewMap();
            Fill(map, 26, 24, 5, 2); Fill(map, 20, 34, 2, 5);
            var w = new World(map);
            PlayerBase(w, true, true);
            w.GiveResources(0, 400);
            Troops(w, UnitKind.Rifleman, 4, 12, 22, 0, false);
            w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
            ShrinkEnemyHq(w, 4000);
            w.PlaceBuilding(BuildingKind.PowerPlant, 44, 52, 1);
            w.PlaceBuilding(BuildingKind.Barracks, 50, 44, 1);
            w.PlaceBuilding(BuildingKind.VehicleFactory, 41, 44, 1);
            Troops(w, UnitKind.Rifleman, 6, 44, 40, 1, true);
            Troops(w, UnitKind.RocketTrooper, 2, 48, 40, 1, true);
            Troops(w, UnitKind.Jeep, 1, 40, 48, 1, true);
            return w;
        }

        /// <summary>Two long rock walls with one gap between them.</summary>
        private static World Canyon()
        {
            var map = NewMap();
            Fill(map, 30, 0, 4, 26);
            Fill(map, 30, 38, 4, 26);
            var w = new World(map);
            PlayerBase(w, false, true);
            w.GiveResources(0, 300);
            Troops(w, UnitKind.Rifleman, 8, 12, 22, 0, false);
            w.PlaceBuilding(BuildingKind.Hq, 52, 52, 1);
            w.PlaceBuilding(BuildingKind.PowerPlant, 44, 54, 1);
            w.PlaceBuilding(BuildingKind.Barracks, 52, 44, 1);
            return w;
        }

        /// <summary>The enemy base sits behind rock walls with two gates, guarded by towers.</summary>
        private static World Fortress()
        {
            var map = NewMap();
            Fill(map, 34, 36, 2, 12);   // wall, west side of the fortress
            Fill(map, 34, 52, 2, 12);
            Fill(map, 36, 36, 20, 2);   // wall, south side
            Fill(map, 60, 36, 4, 2);
            var w = new World(map);
            PlayerBase(w, true, true);
            w.SetBaseLevel(0, 2);
            w.GiveResources(0, 400);
            Troops(w, UnitKind.Rifleman, 6, 12, 22, 0, false);
            w.PlaceBuilding(BuildingKind.Hq, 50, 52, 1);
            ShrinkEnemyHq(w, 10000);
            w.PlaceBuilding(BuildingKind.PowerPlant, 44, 54, 1);
            w.PlaceBuilding(BuildingKind.Barracks, 44, 44, 1);
            w.PlaceBuilding(BuildingKind.Tower, 38, 49, 1);
            w.PlaceBuilding(BuildingKind.Tower, 53, 39, 1);
            Troops(w, UnitKind.Rifleman, 4, 40, 44, 1, true);
            return w;
        }
    }

    /// <summary>
    /// What the player has finished: best stars per mission and the credits earned. Stored as a short text
    /// ("3,2,0,...|credits") so it can live in a preference now and on the server later.
    /// </summary>
    public sealed class CampaignProgress
    {
        private readonly int[] _stars;

        // The wallet is three counters that only ever grow, so that two copies (two phones) can be joined without losing anything:
        // credits earned in play, credits spent in the armory, credits bought in the store. What is left is earned + bought - spent.
        private int _earned, _spent, _bought;

        public int Credits => Math.Max(0, _earned + _bought - _spent);

        public CampaignProgress()
        {
            _stars = new int[Campaign.Missions.Count];
        }

        public int Stars(int mission) => _stars[mission - 1];

        public int TotalStars
        {
            get { int sum = 0; foreach (int s in _stars) sum += s; return sum; }
        }

        /// <summary>The first mission is open from the start; the next ones open once the one before has been won.</summary>
        /// <summary>Online matches open once the first <see cref="Campaign.OnlineUnlockMission"/> missions are won: by then a player knows the controls and the units.</summary>
        public bool OnlineUnlocked => Stars(Campaign.OnlineUnlockMission) > 0;

        public bool IsUnlocked(int mission) => mission == 1 || (mission >= 2 && mission <= _stars.Length && _stars[mission - 2] > 0);

        /// <summary>The first mission that has not been won yet (the last one when everything is done).</summary>
        public int NextMission
        {
            get
            {
                for (int i = 0; i < _stars.Length; i++) if (_stars[i] == 0) return i + 1;
                return _stars.Length;
            }
        }

        /// <summary>Adds credits earned by playing (missions, daily rewards) or given for development.</summary>
        public void Add(int amount)
        {
            if (amount > 0) _earned += amount;
        }

        /// <summary>Adds credits the player paid for. Kept apart from <see cref="Add"/> only so that joining two copies never loses them.</summary>
        public void AddPurchased(int amount)
        {
            if (amount > 0) _bought += amount;
        }

        /// <summary>Takes credits out of the wallet (the armory). False, and nothing changes, when there are not enough.</summary>
        public bool Spend(int amount)
        {
            if (amount < 0 || amount > Credits) return false;
            _spent += amount;
            return true;
        }

        /// <summary>Records a finished mission and returns the credits it paid: each star is paid once, the first time it is earned.</summary>
        public int Record(int mission, int stars)
        {
            if (mission < 1 || mission > _stars.Length || stars <= 0) return 0;
            if (stars > 3) stars = 3;
            int newStars = stars - _stars[mission - 1];
            if (newStars <= 0) return 0;
            int paid = newStars * Campaign.Get(mission).CreditsPerStar;
            _stars[mission - 1] = stars;
            _earned += paid;
            return paid;
        }

        /// <summary>Joins another copy of the same player's progress (another device): the best stars of each mission and the larger of each wallet counter.</summary>
        public void MergeFrom(CampaignProgress other)
        {
            for (int i = 0; i < _stars.Length; i++) _stars[i] = Math.Max(_stars[i], other._stars[i]);
            _earned = Math.Max(_earned, other._earned);
            _spent = Math.Max(_spent, other._spent);
            _bought = Math.Max(_bought, other._bought);
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _stars.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_stars[i]);
            }
            return sb.Append('|').Append(_earned).Append('|').Append(_spent).Append('|').Append(_bought).ToString();
        }

        /// <summary>Reads what <see cref="Serialize"/> wrote; anything unreadable gives an empty progress rather than an error.</summary>
        public static CampaignProgress Parse(string text)
        {
            var progress = new CampaignProgress();
            if (string.IsNullOrEmpty(text)) return progress;
            string[] halves = text.Split('|');
            string[] parts = halves[0].Split(',');
            for (int i = 0; i < parts.Length && i < progress._stars.Length; i++)
                if (int.TryParse(parts[i], out int s)) progress._stars[i] = Math.Max(0, Math.Min(3, s));
            // "stars|earned" is the old form (one number); "stars|earned|spent|bought" the current one.
            if (halves.Length > 1 && int.TryParse(halves[1], out int earned)) progress._earned = Math.Max(0, earned);
            if (halves.Length > 3)
            {
                if (int.TryParse(halves[2], out int spent)) progress._spent = Math.Max(0, spent);
                if (int.TryParse(halves[3], out int bought)) progress._bought = Math.Max(0, bought);
            }
            return progress;
        }
    }
}
