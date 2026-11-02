using System;

namespace SimCore
{
    public enum UnitKind { Rifleman, RocketTrooper, Jeep, Tank }

    public enum UnitClass { Infantry, Vehicle }

    public enum BuildingKind { Hq, PowerPlant, SupplyCenter, Barracks, VehicleFactory }

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

        public BuildingDef(BuildingKind kind, string name, int width, int height)
        {
            Kind = kind; Name = name; Width = width; Height = height;
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
                case BuildingKind.Hq: return new BuildingDef(kind, "Baza", 4, 4);
                case BuildingKind.PowerPlant: return new BuildingDef(kind, "Elektrik stansiyası", 2, 2);
                case BuildingKind.SupplyCenter: return new BuildingDef(kind, "Təchizat mərkəzi", 3, 3);
                case BuildingKind.Barracks: return new BuildingDef(kind, "Kazarma", 3, 2);
                case BuildingKind.VehicleFactory: return new BuildingDef(kind, "Texnika zavodu", 4, 3);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
