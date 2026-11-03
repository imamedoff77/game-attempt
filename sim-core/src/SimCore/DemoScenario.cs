namespace SimCore
{
    /// <summary>
    /// The demo's starting situation: desert map, the player's base and squad, and a guarded enemy base.
    /// Lives in sim-core (not in Unity) so tests can play the same scenario the phone shows.
    /// </summary>
    public static class DemoScenario
    {
        public const int MapSize = 64;
        public const ulong MapSeed = 2026;
        public const int StartX = 26, StartY = 26;

        public static World Create()
        {
            MapGrid map = DemoMap.Generate(MapSize, MapSize, MapSeed, StartX, StartY, 6);
            var world = new World(map);
            world.PlaceBuilding(BuildingKind.Hq, 22, 21); // everything else the player builds
            SpawnPlayerSquad(world);
            SpawnEnemyBase(world);
            return world;
        }

        /// <summary>Three of each kind: one column per kind, south of the base (inside the cleared start area).</summary>
        private static void SpawnPlayerSquad(World world)
        {
            UnitKind[] order = { UnitKind.Rifleman, UnitKind.RocketTrooper, UnitKind.Jeep, UnitKind.Tank };
            for (int col = 0; col < order.Length; col++)
                for (int i = 0; i < 3; i++)
                    world.SpawnUnit(order[col], Cell(23 + col * 2, 30 + i));
        }

        /// <summary>
        /// Small enemy base in the north-west: guards in front of it, a patrolling jeep, no production.
        /// Everything here stands on sand (checked by tests).
        /// </summary>
        private static void SpawnEnemyBase(World world)
        {
            int e = World.TeamEnemy;
            world.PlaceBuilding(BuildingKind.Hq, 14, 52, e);
            world.PlaceBuilding(BuildingKind.PowerPlant, 20, 52, e);
            world.PlaceBuilding(BuildingKind.SupplyCenter, 20, 56, e);
            world.PlaceBuilding(BuildingKind.Barracks, 10, 56, e);
            world.PlaceBuilding(BuildingKind.VehicleFactory, 8, 48, e);

            UnitKind[] guards =
            {
                UnitKind.Rifleman, UnitKind.Rifleman, UnitKind.Rifleman, UnitKind.Rifleman,
                UnitKind.RocketTrooper, UnitKind.RocketTrooper, UnitKind.Tank, UnitKind.Jeep,
            };
            for (int i = 0; i < guards.Length; i++)
                world.SetGuard(world.SpawnUnit(guards[i], Cell(14 + (i % 4) * 2, 46 + i / 4 * 2), e));

            Unit patrol = world.SpawnUnit(UnitKind.Jeep, new Vec2(Fix64.FromInt(26), Fix64.FromInt(44)), e);
            world.SetGuard(patrol, new[]
            {
                new Vec2(Fix64.FromInt(26), Fix64.FromInt(44)),
                new Vec2(Fix64.FromInt(10), Fix64.FromInt(44)),
            });
        }

        private static Vec2 Cell(int x, int y) => new Vec2(Fix64.FromInt(x) + Fix64.Half, Fix64.FromInt(y) + Fix64.Half);
    }
}
