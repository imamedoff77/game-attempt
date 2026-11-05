using System;

namespace SimCore
{
    public enum UnitKind { Rifleman, RocketTrooper, Jeep, Tank, Sniper, ArmoredCar }

    public enum UnitClass { Infantry, Vehicle }

    public enum BuildingKind { Hq, PowerPlant, SupplyCenter, Barracks, VehicleFactory, RepairStation, Tower }

    public enum AttackKind { None, Unit, Building }

    /// <summary>Researchable improvements. Each has several tiers; every finished tier adds the same bonus.</summary>
    public enum UpgradeKind { InfantryDamage, InfantryHealth, VehicleDamage, VehicleHealth }

    public enum UpgradeStat { Damage, Health }

    public enum ResearchCheck { Ok, NotAllowed, MaxTier, AlreadyResearching, NotEnoughResources, LevelTooLow }

    /// <summary>Fighting stats of a unit kind.</summary>
    public readonly struct CombatDef
    {
        public int Hp { get; }
        public int Damage { get; }
        /// <summary>Shooting distance in map units.</summary>
        public Fix64 Range { get; }
        /// <summary>Ticks between shots.</summary>
        public int CooldownTicks { get; }
        /// <summary>How far it notices enemies, in map units.</summary>
        public Fix64 Sight { get; }
        /// <summary>Width of the firing arc in degrees (360 = all around). While moving, a unit can only shoot inside this arc, centred on the way it faces.</summary>
        public int ArcDegrees { get; }
        /// <summary>cos(ArcDegrees / 2), precomputed so the simulation needs no trigonometry.</summary>
        public Fix64 CosHalfArc { get; }

        /// <summary>Damage dealt to infantry, vehicles and buildings, in thousandths of the base damage (1000 = full).</summary>
        public int VsInfantryMilli { get; }
        public int VsVehicleMilli { get; }
        public int VsBuildingMilli { get; }

        public CombatDef(int hp, int damage, int range, int cooldownTicks, int sight, int arcDegrees, int cosHalfArcThousandths,
            int vsInfantryMilli = 1000, int vsVehicleMilli = 1000, int vsBuildingMilli = 1000)
        {
            VsInfantryMilli = vsInfantryMilli; VsVehicleMilli = vsVehicleMilli; VsBuildingMilli = vsBuildingMilli;
            Hp = hp; Damage = damage; Range = Fix64.FromInt(range); CooldownTicks = cooldownTicks; Sight = Fix64.FromInt(sight);
            ArcDegrees = arcDegrees; CosHalfArc = Fix64.FromFraction(cosHalfArcThousandths, 1000);
        }
    }

    public enum QueueCheck { Ok, NotAllowed, QueueFull, NotEnoughResources, PopCapReached, LevelTooLow }

    public enum UpgradeCheck { Ok, NotAllowed, MaxLevel, AlreadyUpgrading, NotEnoughResources }

    public enum BuildCheck { Ok, NotAllowed, BadGround, UnitInWay, NotEnoughResources, LevelTooLow }

    public readonly struct UnitDef
    {
        public UnitKind Kind { get; }
        public UnitClass Class { get; }
        /// <summary>Map units per second.</summary>
        public Fix64 Speed { get; }
        /// <summary>Resource price, paid when it is queued.</summary>
        public int Cost { get; }
        /// <summary>Seconds of production time (while the producing building has energy).</summary>
        public int BuildSeconds { get; }
        public BuildingKind Producer { get; }
        /// <summary>Places it takes in the base's unit limit.</summary>
        public int Pop { get; }
        /// <summary>Body size in map units: two units never overlap more than this (see World.SeparateUnits).</summary>
        public Fix64 Radius { get; }
        /// <summary>Base level the owner needs before this unit can be queued.</summary>
        public int RequiredLevel { get; }

        public UnitDef(UnitKind kind, UnitClass unitClass, Fix64 speed, int cost, int buildSeconds, BuildingKind producer, int pop, Fix64 radius, int requiredLevel)
        {
            Kind = kind; Class = unitClass; Speed = speed; Cost = cost; BuildSeconds = buildSeconds; Producer = producer; Pop = pop; Radius = radius; RequiredLevel = requiredLevel;
        }
    }

    /// <summary>A kind of improvement: which units it affects, what it changes, and the price of every tier.</summary>
    public sealed class UpgradeDef
    {
        public UpgradeKind Kind { get; }
        public UnitClass Class { get; }
        public UpgradeStat Stat { get; }
        /// <summary>Gain per finished tier in thousandths (100 = +10 %). Whole numbers keep every device identical.</summary>
        public int BonusMilli { get; }
        public int[] Cost { get; }
        public int[] Seconds { get; }
        /// <summary>Base level needed to research each tier.</summary>
        public int[] RequiredLevel { get; }
        public int Tiers => Cost.Length;

        public UpgradeDef(UpgradeKind kind, UnitClass unitClass, UpgradeStat stat, int bonusMilli, int[] cost, int[] seconds, int[] requiredLevel)
        {
            Kind = kind; Class = unitClass; Stat = stat; BonusMilli = bonusMilli; Cost = cost; Seconds = seconds; RequiredLevel = requiredLevel;
        }
    }

    public readonly struct BuildingDef
    {
        public BuildingKind Kind { get; }
        /// <summary>Footprint in cells.</summary>
        public int Width { get; }
        public int Height { get; }
        /// <summary>Resource price.</summary>
        public int Cost { get; }
        /// <summary>Energy this building needs in order to work (0 = needs none).</summary>
        public int EnergyUse { get; }
        /// <summary>Seconds a new building takes to be built (0 = ready at once).</summary>
        public int ConstructionSeconds { get; }
        /// <summary>Base level the owner needs before this building can be placed.</summary>
        public int RequiredLevel { get; }
        /// <summary>Defence buildings shoot the nearest enemy unit in <see cref="Range"/> (Damage 0 = does not shoot).</summary>
        public int Damage { get; }
        public Fix64 Range { get; }
        public int CooldownTicks { get; }
        /// <summary>Repair buildings heal friendly units within <see cref="RepairRadius"/> by this much each second (0 = does not repair).</summary>
        public int RepairPerSecond { get; }
        public Fix64 RepairRadius { get; }

        public BuildingDef(BuildingKind kind, int width, int height, int cost, int energyUse, int constructionSeconds,
            int requiredLevel, int damage, int range, int cooldownTicks, int repairPerSecond, int repairRadius)
        {
            Kind = kind; Width = width; Height = height; Cost = cost; EnergyUse = energyUse; ConstructionSeconds = constructionSeconds;
            RequiredLevel = requiredLevel; Damage = damage; Range = Fix64.FromInt(range); CooldownTicks = cooldownTicks;
            RepairPerSecond = repairPerSecond; RepairRadius = Fix64.FromInt(repairRadius);
        }
    }

    /// <summary>
    /// Balance numbers, served from the loaded <see cref="GameData"/> (gamedata.json).
    /// The host must call <see cref="Load"/> once at start-up, before any World is created.
    /// Display names are not here: the simulation does not care about them (see the client's string table).
    /// </summary>
    public static class Defs
    {
        private static GameData _data;

        public static bool IsLoaded => _data != null;

        /// <summary>Fingerprint of the loaded balance data; both players of a match must have the same value.</summary>
        public static ulong DataHash => Data.Hash;

        /// <summary>Reads gamedata.json text. Throws <see cref="GameDataException"/> if it is invalid (the previous data stays in use).</summary>
        public static void Load(string json) => _data = GameData.Parse(json);

        internal static GameData Data
        {
            get
            {
                if (_data == null) throw new InvalidOperationException("Balance data is not loaded: call Defs.Load(gamedata.json text) before using the simulation.");
                return _data;
            }
        }

        public static UnitDef Unit(UnitKind kind) => Data.Units[(int)kind];

        public static CombatDef Combat(UnitKind kind) => Data.Combat[(int)kind];

        public static UpgradeDef Upgrade(UpgradeKind kind) => Data.Upgrades[(int)kind];

        public static int UpgradeCount => Data.Upgrades.Length;

        public static int BuildingHp(BuildingKind kind) => Data.BuildingHp[(int)kind];

        /// <summary>The units a building can produce (empty for buildings that produce nothing). Do not modify the result.</summary>
        public static UnitKind[] Produces(BuildingKind kind) => Data.Produces[(int)kind];

        public static BuildingDef Building(BuildingKind kind) => Data.Buildings[(int)kind];
    }

    /// <summary>Economy numbers, served from the loaded <see cref="GameData"/>.</summary>
    public static class Economy
    {
        public static int StartResources => Defs.Data.StartResources;
        public static int HqEnergy => Defs.Data.HqEnergy;
        public static int HqIncomePerMinute => Defs.Data.HqIncomePerMinute;

        /// <summary>Energy given by the 1st, 2nd, 3rd... power plant. Later ones repeat the last value.</summary>
        public static int[] PowerPlantOutput => Defs.Data.PowerPlantOutput;

        /// <summary>Resources per minute given by the 1st, 2nd, 3rd... supply center. Later ones repeat the last value.</summary>
        public static int[] SupplyCenterIncome => Defs.Data.SupplyCenterIncome;

        /// <summary>Highest base level.</summary>
        public static int MaxBaseLevel => Defs.Data.PopCap.Length;
        /// <summary>Unit places of a base of the given level (1-based).</summary>
        public static int PopCap(int baseLevel) => Defs.Data.PopCap[baseLevel - 1];
        /// <summary>Price of the upgrade from <paramref name="fromLevel"/> to the next level.</summary>
        public static int BaseUpgradeCost(int fromLevel) => Defs.Data.BaseUpgradeCost[fromLevel - 1];
        public static int BaseUpgradeSeconds(int fromLevel) => Defs.Data.BaseUpgradeSeconds[fromLevel - 1];
        public static int CancelRefundPercent => Defs.Data.CancelRefundPercent;

        public static int Pick(int[] table, int index) => table[index < table.Length ? index : table.Length - 1];
    }
}
