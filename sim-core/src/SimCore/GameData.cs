using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// All balance numbers (health, damage, prices, times, income tables), read from gamedata.json.
    /// Every number is a whole number: speeds are in thousandths of a map unit per second, so no float ever reaches the simulation.
    /// Both players of a match must use data with the same <see cref="Hash"/>.
    /// </summary>
    public sealed class GameData
    {
        public const int CurrentVersion = 1;

        public int StartResources { get; private set; }
        public int HqEnergy { get; private set; }
        public int HqIncomePerMinute { get; private set; }
        public int[] PowerPlantOutput { get; private set; }
        public int[] SupplyCenterIncome { get; private set; }
        /// <summary>Unit places a base of level 1, 2, 3... provides.</summary>
        public int[] PopCap { get; private set; }
        /// <summary>Price and time to raise the base from level 1 to 2, from 2 to 3...</summary>
        public int[] BaseUpgradeCost { get; private set; }
        public int[] BaseUpgradeSeconds { get; private set; }
        /// <summary>Percent of the price given back when a building under construction is cancelled.</summary>
        public int CancelRefundPercent { get; private set; }

        internal UnitDef[] Units;
        internal CombatDef[] Combat;
        internal BuildingDef[] Buildings;
        internal int[] BuildingHp;
        internal UnitKind[][] Produces;
        internal UpgradeDef[] Upgrades;

        /// <summary>Fingerprint of every value, independent of spacing and order in the file.</summary>
        public ulong Hash { get; private set; }

        public static GameData Parse(string json)
        {
            object root = MiniJson.Parse(json);
            var data = new GameData();
            data.Read(root);
            data.Hash = data.ComputeHash();
            return data;
        }

        // ---- reading -------------------------------------------------------------------------------------------

        private void Read(object rootValue)
        {
            var root = new Obj(rootValue, "file");
            int version = root.Int("version", 1, int.MaxValue);
            if (version != CurrentVersion)
                throw new GameDataException($"file: version {version} is not supported (expected {CurrentVersion})");

            var economy = new Obj(root.Raw("economy"), "economy");
            StartResources = economy.Int("startResources", 0, 1000000);
            HqEnergy = economy.Int("hqEnergy", 0, 100000);
            HqIncomePerMinute = economy.Int("hqIncomePerMinute", 0, 1000000);
            PowerPlantOutput = economy.IntList("powerPlantOutput", 0, 100000);
            SupplyCenterIncome = economy.IntList("supplyCenterIncome", 0, 1000000);
            PopCap = economy.IntList("popCap", 1, 10000);
            BaseUpgradeCost = economy.IntList("baseUpgradeCost", 0, 1000000);
            BaseUpgradeSeconds = economy.IntList("baseUpgradeSeconds", 1, 3600);
            CancelRefundPercent = economy.Int("cancelRefundPercent", 0, 100);
            economy.Finish();
            if (BaseUpgradeCost.Length != PopCap.Length - 1 || BaseUpgradeSeconds.Length != PopCap.Length - 1)
                throw new GameDataException("economy: baseUpgradeCost and baseUpgradeSeconds need one entry fewer than popCap (one per upgrade between levels)");

            Units = new UnitDef[EnumCount<UnitKind>()];
            Combat = new CombatDef[Units.Length];
            var seenUnits = new bool[Units.Length];
            var unitList = Items(root.Raw("units"), "units");
            for (int i = 0; i < unitList.Count; i++) ReadUnit(unitList[i], "units[" + i + "]", seenUnits);
            for (int k = 0; k < seenUnits.Length; k++)
                if (!seenUnits[k]) throw new GameDataException("units: missing entry for " + (UnitKind)k);

            Buildings = new BuildingDef[EnumCount<BuildingKind>()];
            BuildingHp = new int[Buildings.Length];
            var seenBuildings = new bool[Buildings.Length];
            var buildingList = Items(root.Raw("buildings"), "buildings");
            for (int i = 0; i < buildingList.Count; i++) ReadBuilding(buildingList[i], "buildings[" + i + "]", seenBuildings);
            for (int k = 0; k < seenBuildings.Length; k++)
                if (!seenBuildings[k]) throw new GameDataException("buildings: missing entry for " + (BuildingKind)k);

            Upgrades = new UpgradeDef[EnumCount<UpgradeKind>()];
            var seenUpgrades = new bool[Upgrades.Length];
            var upgradeList = Items(root.Raw("upgrades"), "upgrades");
            for (int i = 0; i < upgradeList.Count; i++) ReadUpgrade(upgradeList[i], "upgrades[" + i + "]", seenUpgrades);
            for (int k = 0; k < seenUpgrades.Length; k++)
                if (!seenUpgrades[k]) throw new GameDataException("upgrades: missing entry for " + (UpgradeKind)k);

            root.Finish();

            // Which units a building makes follows from each unit's "producer", in file order.
            Produces = new UnitKind[Buildings.Length][];
            for (int b = 0; b < Produces.Length; b++)
            {
                var list = new List<UnitKind>();
                for (int u = 0; u < Units.Length; u++)
                    if (Units[u].Producer == (BuildingKind)b) list.Add((UnitKind)u);
                Produces[b] = list.ToArray();
            }
        }

        private void ReadUnit(object value, string path, bool[] seen)
        {
            var o = new Obj(value, path);
            UnitKind kind = o.Enum<UnitKind>("id");
            if (seen[(int)kind]) throw new GameDataException($"{path}: {kind} is listed twice");
            seen[(int)kind] = true;

            UnitClass unitClass = o.Enum<UnitClass>("class");
            int speedMilli = o.Int("speedMilli", 1, 1000000);
            int cost = o.Int("cost", 0, 1000000);
            int buildSeconds = o.Int("buildSeconds", 1, 3600);
            int pop = o.Int("pop", 1, 100);
            int radiusMilli = o.Int("radiusMilli", 100, 900);
            int requiredLevel = o.IntOr("requiredLevel", 1, 1, 10);
            BuildingKind producer = o.Enum<BuildingKind>("producer");
            int hp = o.Int("hp", 1, 1000000);
            int damage = o.Int("damage", 0, 1000000);
            int range = o.Int("range", 1, 1000);
            int cooldown = o.Int("cooldownTicks", 1, 1000);
            int sight = o.Int("sight", 1, 1000);
            int arc = o.Int("arcDegrees", 1, 360);
            int cosHalfArc = o.Int("cosHalfArcMilli", -1000, 1000);
            int vsInfantry = o.IntOr("vsInfantryMilli", 1000, 0, 10000);
            int vsVehicle = o.IntOr("vsVehicleMilli", 1000, 0, 10000);
            int vsBuilding = o.IntOr("vsBuildingMilli", 1000, 0, 10000);
            o.Finish();

            Units[(int)kind] = new UnitDef(kind, unitClass, Fix64.FromFraction(speedMilli, 1000), cost, buildSeconds, producer, pop, Fix64.FromFraction(radiusMilli, 1000), requiredLevel);
            Combat[(int)kind] = new CombatDef(hp, damage, range, cooldown, sight, arc, cosHalfArc, vsInfantry, vsVehicle, vsBuilding);
        }

        private void ReadUpgrade(object value, string path, bool[] seen)
        {
            var o = new Obj(value, path);
            UpgradeKind kind = o.Enum<UpgradeKind>("id");
            if (seen[(int)kind]) throw new GameDataException($"{path}: {kind} is listed twice");
            seen[(int)kind] = true;

            UnitClass unitClass = o.Enum<UnitClass>("class");
            UpgradeStat stat = o.Enum<UpgradeStat>("stat");
            int bonus = o.Int("bonusMilli", 1, 10000);
            int[] cost = o.IntList("cost", 0, 1000000);
            int[] seconds = o.IntList("seconds", 1, 3600);
            int[] level = o.IntList("requiredLevel", 1, 10);
            o.Finish();
            if (seconds.Length != cost.Length || level.Length != cost.Length)
                throw new GameDataException($"{path}: cost, seconds and requiredLevel need the same number of tiers");

            Upgrades[(int)kind] = new UpgradeDef(kind, unitClass, stat, bonus, cost, seconds, level);
        }

        private void ReadBuilding(object value, string path, bool[] seen)
        {
            var o = new Obj(value, path);
            BuildingKind kind = o.Enum<BuildingKind>("id");
            if (seen[(int)kind]) throw new GameDataException($"{path}: {kind} is listed twice");
            seen[(int)kind] = true;

            int width = o.Int("width", 1, 64);
            int height = o.Int("height", 1, 64);
            int cost = o.Int("cost", 0, 1000000);
            int energyUse = o.Int("energyUse", 0, 100000);
            int hp = o.Int("hp", 1, 1000000);
            int constructionSeconds = o.Int("buildSeconds", 0, 3600);
            int requiredLevel = o.IntOr("requiredLevel", 1, 1, 10);
            int damage = o.IntOr("damage", 0, 0, 1000000);
            int range = o.IntOr("range", 0, 0, 1000);
            int cooldown = o.IntOr("cooldownTicks", 0, 0, 1000);
            int repairPerSecond = o.IntOr("repairPerSecond", 0, 0, 100000);
            int repairRadius = o.IntOr("repairRadius", 0, 0, 1000);
            o.Finish();
            if (damage > 0 && (range == 0 || cooldown == 0)) throw new GameDataException($"{path}: a shooting building needs range and cooldownTicks");
            if (repairPerSecond > 0 && repairRadius == 0) throw new GameDataException($"{path}: a repair building needs repairRadius");

            Buildings[(int)kind] = new BuildingDef(kind, width, height, cost, energyUse, constructionSeconds,
                requiredLevel, damage, range, cooldown, repairPerSecond, repairRadius);
            BuildingHp[(int)kind] = hp;
        }

        private static List<object> Items(object value, string path)
        {
            if (!(value is List<object> list)) throw new GameDataException(path + ": expected a list");
            return list;
        }

        private static int EnumCount<T>() where T : struct => Enum.GetValues(typeof(T)).Length;

        // ---- hashing -------------------------------------------------------------------------------------------

        private ulong ComputeHash()
        {
            var h = StateHasher.Create();
            h.Add(CurrentVersion);
            h.Add(StartResources); h.Add(HqEnergy); h.Add(HqIncomePerMinute);
            AddTable(ref h, PowerPlantOutput);
            AddTable(ref h, SupplyCenterIncome);
            foreach (UpgradeDef u in Upgrades)
            {
                h.Add((int)u.Kind); h.Add((int)u.Class); h.Add((int)u.Stat); h.Add(u.BonusMilli);
                AddTable(ref h, u.Cost); AddTable(ref h, u.Seconds); AddTable(ref h, u.RequiredLevel);
            }
            AddTable(ref h, PopCap); AddTable(ref h, BaseUpgradeCost); AddTable(ref h, BaseUpgradeSeconds); h.Add(CancelRefundPercent);
            for (int i = 0; i < Units.Length; i++)
            {
                UnitDef u = Units[i];
                CombatDef c = Combat[i];
                h.Add(i); h.Add((int)u.Class); h.Add(u.Speed); h.Add(u.Cost); h.Add(u.BuildSeconds); h.Add((int)u.Producer); h.Add(u.Pop); h.Add(u.Radius); h.Add(u.RequiredLevel);
                h.Add(c.Hp); h.Add(c.Damage); h.Add(c.Range); h.Add(c.CooldownTicks); h.Add(c.Sight); h.Add(c.ArcDegrees); h.Add(c.CosHalfArc);
                h.Add(c.VsInfantryMilli); h.Add(c.VsVehicleMilli); h.Add(c.VsBuildingMilli);
            }
            for (int i = 0; i < Buildings.Length; i++)
            {
                BuildingDef b = Buildings[i];
                h.Add(i); h.Add(b.Width); h.Add(b.Height); h.Add(b.Cost); h.Add(b.EnergyUse); h.Add(BuildingHp[i]); h.Add(b.ConstructionSeconds);
                h.Add(b.RequiredLevel); h.Add(b.Damage); h.Add(b.Range); h.Add(b.CooldownTicks); h.Add(b.RepairPerSecond); h.Add(b.RepairRadius);
            }
            return h.Value;
        }

        private static void AddTable(ref StateHasher h, int[] table)
        {
            h.Add(table.Length);
            for (int i = 0; i < table.Length; i++) h.Add(table[i]);
        }

        // ---- strict object reader ------------------------------------------------------------------------------

        /// <summary>Reads named fields of a JSON object, rejects wrong types, out-of-range numbers and unknown names.</summary>
        private sealed class Obj
        {
            private readonly Dictionary<string, object> _map;
            private readonly HashSet<string> _used = new HashSet<string>();
            private readonly string _path;

            public Obj(object value, string path)
            {
                _path = path;
                _map = value as Dictionary<string, object>;
                if (_map == null) throw new GameDataException(path + ": expected an object { ... }");
            }

            public object Raw(string name)
            {
                if (!_map.TryGetValue(name, out object v)) throw new GameDataException($"{_path}: missing \"{name}\"");
                _used.Add(name);
                return v;
            }

            public int Int(string name, int min, int max)
            {
                object v = Raw(name);
                if (!(v is long n)) throw new GameDataException($"{_path}.{name}: expected a whole number");
                if (n < min || n > max) throw new GameDataException($"{_path}.{name}: {n} is outside the allowed range {min}..{max}");
                return (int)n;
            }

            /// <summary>Like <see cref="Int"/>, but the field may be left out (then <paramref name="fallback"/> is used).</summary>
            public int IntOr(string name, int fallback, int min, int max) => _map.ContainsKey(name) ? Int(name, min, max) : fallback;

            public int[] IntList(string name, int min, int max)
            {
                var list = Raw(name) as List<object>;
                if (list == null || list.Count == 0) throw new GameDataException($"{_path}.{name}: expected a non-empty list of whole numbers");
                var result = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is long n)) throw new GameDataException($"{_path}.{name}[{i}]: expected a whole number");
                    if (n < min || n > max) throw new GameDataException($"{_path}.{name}[{i}]: {n} is outside the allowed range {min}..{max}");
                    result[i] = (int)n;
                }
                return result;
            }

            public T Enum<T>(string name) where T : struct
            {
                if (!(Raw(name) is string s)) throw new GameDataException($"{_path}.{name}: expected a text value");
                if (!System.Enum.TryParse(s, false, out T result) || !System.Enum.IsDefined(typeof(T), result))
                    throw new GameDataException($"{_path}.{name}: \"{s}\" is not a valid {typeof(T).Name}");
                return result;
            }

            /// <summary>Call after reading every expected field: any leftover name is probably a typo.</summary>
            public void Finish()
            {
                foreach (string key in _map.Keys)
                    if (!_used.Contains(key)) throw new GameDataException($"{_path}: unknown name \"{key}\"");
            }
        }
    }
}
