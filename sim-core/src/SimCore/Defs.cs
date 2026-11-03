using System;

namespace SimCore
{
    public enum UnitKind { Rifleman, RocketTrooper, Jeep, Tank }

    public enum UnitClass { Infantry, Vehicle }

    public enum BuildingKind { Hq, PowerPlant, SupplyCenter, Barracks, VehicleFactory }

    public enum BuildCheck { Ok, NotAllowed, BadGround, UnitInWay, NotEnoughResources }

    public readonly struct UnitDef
    {
        public UnitKind Kind { get; }
        public UnitClass Class { get; }
        public string Name { get; }
        /// <summary>Map units per second.</summary>
        public Fix64 Speed { get; }

        public UnitDef(UnitKind kind, UnitClass unitClass, string name, Fix64 speed)
        {
            Kind = kind; Class = unitClass; Name = name; Speed = speed;
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
                case UnitKind.Rifleman: return new UnitDef(kind, UnitClass.Infantry, "Əsgər", Fix64.FromInt(3));
                case UnitKind.RocketTrooper: return new UnitDef(kind, UnitClass.Infantry, "Raketçi", Fix64.FromFraction(5, 2));
                case UnitKind.Jeep: return new UnitDef(kind, UnitClass.Vehicle, "Cip", Fix64.FromInt(6));
                case UnitKind.Tank: return new UnitDef(kind, UnitClass.Vehicle, "Tank", Fix64.FromInt(2));
                default: throw new ArgumentOutOfRangeException(nameof(kind));
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
