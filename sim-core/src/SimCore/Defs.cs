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

        public CombatDef(int hp, int damage, int range, int cooldownTicks, int sight)
        {
            Hp = hp; Damage = damage; Range = Fix64.FromInt(range); CooldownTicks = cooldownTicks; Sight = Fix64.FromInt(sight);
        }
    }

    public enum QueueCheck { Ok, NotAllowed, QueueFull, NotEnoughResources }

    public enum BuildCheck { Ok, NotAllowed, BadGround, UnitInWay, NotEnoughResources }

    public readonly struct UnitDef
    {
        public UnitKind Kind { get; }
        public UnitClass Class { get; }
        public string Name { get; }
        /// <summary>Map units per second.</summary>
        public Fix64 Speed { get; }
        /// <summary>Resource price, paid when it is queued.</summary>
        public int Cost { get; }
        /// <summary>Seconds of production time (while the producing building has energy).</summary>
        public int BuildSeconds { get; }
        public BuildingKind Producer { get; }

        public UnitDef(UnitKind kind, UnitClass unitClass, string name, Fix64 speed, int cost, int buildSeconds, BuildingKind producer)
        {
            Kind = kind; Class = unitClass; Name = name; Speed = speed; Cost = cost; BuildSeconds = buildSeconds; Producer = producer;
        }
    }

    public readonly struct BuildingDef
    {
        public BuildingKind Kind { get; }
        public string Name { get; }
        /// <summary>Footprint in cells.</summary>
        public int Width { get; }
        public int Height { get; }
        /// <summary>Resource price.</summary>
        public int Cost { get; }
        /// <summary>Energy this building needs in order to work (0 = needs none).</summary>
        public int EnergyUse { get; }

        public BuildingDef(BuildingKind kind, string name, int width, int height, int cost, int energyUse)
        {
            Kind = kind; Name = name; Width = width; Height = height; Cost = cost; EnergyUse = energyUse;
        }
    }

    /// <summary>Placeholder balance numbers. Later these move to a data file.</summary>
    public static class Defs
    {
        public static UnitDef Unit(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Rifleman: return new UnitDef(kind, UnitClass.Infantry, "Əsgər", Fix64.FromInt(3), 50, 4, BuildingKind.Barracks);
                case UnitKind.RocketTrooper: return new UnitDef(kind, UnitClass.Infantry, "Raketçi", Fix64.FromFraction(5, 2), 80, 6, BuildingKind.Barracks);
                case UnitKind.Jeep: return new UnitDef(kind, UnitClass.Vehicle, "Cip", Fix64.FromInt(6), 150, 8, BuildingKind.VehicleFactory);
                case UnitKind.Tank: return new UnitDef(kind, UnitClass.Vehicle, "Tank", Fix64.FromInt(2), 300, 15, BuildingKind.VehicleFactory);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>Placeholder fighting numbers.</summary>
        public static CombatDef Combat(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Rifleman: return new CombatDef(60, 6, 5, 10, 9);
                case UnitKind.RocketTrooper: return new CombatDef(50, 25, 7, 30, 9);
                case UnitKind.Jeep: return new CombatDef(100, 8, 6, 8, 10);
                case UnitKind.Tank: return new CombatDef(400, 40, 8, 30, 11);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static int BuildingHp(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Hq: return 1500;
                case BuildingKind.PowerPlant: return 400;
                case BuildingKind.SupplyCenter: return 600;
                case BuildingKind.Barracks: return 700;
                case BuildingKind.VehicleFactory: return 900;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static readonly UnitKind[] BarracksUnits = { UnitKind.Rifleman, UnitKind.RocketTrooper };
        private static readonly UnitKind[] FactoryUnits = { UnitKind.Jeep, UnitKind.Tank };
        private static readonly UnitKind[] NoUnits = new UnitKind[0];

        /// <summary>The units a building can produce (empty for buildings that produce nothing).</summary>
        public static UnitKind[] Produces(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return BarracksUnits;
                case BuildingKind.VehicleFactory: return FactoryUnits;
                default: return NoUnits;
            }
        }

        public static BuildingDef Building(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Hq: return new BuildingDef(kind, "Baza", 4, 4, 0, 0);
                case BuildingKind.PowerPlant: return new BuildingDef(kind, "Elektrik stansiyası", 2, 2, 100, 0);
                case BuildingKind.SupplyCenter: return new BuildingDef(kind, "Təchizat mərkəzi", 3, 3, 150, 20);
                case BuildingKind.Barracks: return new BuildingDef(kind, "Kazarma", 3, 2, 200, 30);
                case BuildingKind.VehicleFactory: return new BuildingDef(kind, "Texnika zavodu", 4, 3, 300, 50);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    /// <summary>Placeholder economy numbers. Later these move to a data file.</summary>
    public static class Economy
    {
        public const int StartResources = 500;
        public const int HqEnergy = 20;
        public const int HqIncomePerMinute = 20;

        /// <summary>Energy given by the 1st, 2nd, 3rd... power plant. Later ones repeat the last value.</summary>
        public static readonly int[] PowerPlantOutput = { 120, 80, 60, 50, 40 };

        /// <summary>Resources per minute given by the 1st, 2nd, 3rd... supply center. Later ones repeat the last value.</summary>
        public static readonly int[] SupplyCenterIncome = { 60, 40, 30, 25, 20 };

        public static int Pick(int[] table, int index) => table[index < table.Length ? index : table.Length - 1];
    }
}
