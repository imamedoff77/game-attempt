namespace SimCore
{
    /// <summary>The start of a 1v1 match: two bases in opposite corners, a few riflemen each, no computer enemy.</summary>
    public static class OnlineScenario
    {
        public static World Create()
        {
            var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
            // Team 0 bottom-left, team 1 top-right.
            w.PlaceBuilding(BuildingKind.Hq, 8, 8, 0);
            w.PlaceBuilding(BuildingKind.PowerPlant, 16, 8, 0);
            w.PlaceBuilding(BuildingKind.Barracks, 8, 16, 0);
            w.PlaceBuilding(BuildingKind.VehicleFactory, 16, 16, 0);
            w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
            w.PlaceBuilding(BuildingKind.PowerPlant, 44, 50, 1);
            w.PlaceBuilding(BuildingKind.Barracks, 50, 44, 1);
            w.PlaceBuilding(BuildingKind.VehicleFactory, 42, 42, 1);
            for (int i = 0; i < 4; i++)
            {
                w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(12 + i), Fix64.FromInt(24)), 0);
                w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(50 - i), Fix64.FromInt(40)), 1);
            }
            return w;
        }
    }
}
