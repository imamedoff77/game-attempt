using System;

namespace SimCore
{
    public enum UnitKind { Rifleman, RocketTrooper, Jeep, Tank }

    public enum UnitClass { Infantry, Vehicle }

    public enum BuildingKind { Hq, PowerPlant, SupplyCenter, Barracks, VehicleFactory }

    public enum AttackKind { None, Unit, Building }

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

        public CombatDef(int hp, int damage, int range, int cooldownTicks, int sight, int arcDegrees, int cosHalfArcThousandths)
        {
            Hp = hp; Damage = damage; Range = Fix64.FromInt(range); CooldownTicks = cooldownTicks; Sight = Fix64.FromInt(sight);
            ArcDegrees = arcDegrees; CosHalfArc = Fix64.FromFraction(cosHalfArcThousandths, 1000);
        }
    }

    public enum QueueCheck { Ok, NotAllowed, QueueFull, NotEnoughResources }

    public enum BuildCheck { Ok, NotAllowed, BadGround, UnitInWay, NotEnoughResources }

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

        public UnitDef(UnitKind kind, UnitClass unitClass, Fix64 speed, int cost, int buildSeconds, BuildingKind producer)
        {
            Kind = kind; Class = unitClass; Speed = speed; Cost = cost; BuildSeconds = buildSeconds; Producer = producer;
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

        public BuildingDef(BuildingKind kind, int width, int height, int cost, int energyUse)
        {
            Kind = kind; Width = width; Height = height; Cost = cost; EnergyUse = energyUse;
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

        public static int Pick(int[] table, int index) => table[index < table.Length ? index : table.Length - 1];
    }
}
