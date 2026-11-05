namespace SimCore
{
    /// <summary>
    /// A fair 1v1 map for matches against the computer: the two bases are mirror images of each other
    /// (rotated half a turn around the centre), with a few rocks in between so units have to go around.
    /// </summary>
    public static class SkirmishScenario
    {
        public const int Size = 64;

        /// <summary>
        /// <paramref name="seed"/> changes small things (which side's units are created first, where the starting riflemen stand)
        /// so that two equal players do not always get the same result from the tie-breaking order inside the simulation.
        /// Both sides are always treated alike.
        /// </summary>
        public static World Create(ulong seed = 0)
        {
            var rng = new DeterministicRandom(seed * 0x9E3779B97F4A7C15UL + 12345);
            var map = new MapGrid(Size, Size);
            // Rocks between the bases, placed as mirrored pairs.
            int[][] rocks =
            {
                new[] { 26, 24, 5, 2 }, new[] { 20, 30, 2, 4 }, new[] { 14, 36, 4, 2 },
            };
            foreach (int[] r in rocks)
            {
                Fill(map, r[0], r[1], r[2], r[3]);
                Fill(map, Size - r[0] - r[2], Size - r[1] - r[3], r[2], r[3]);
            }

            var w = new World(map);
            Mirror(w, BuildingKind.Hq, 8, 8);
            Mirror(w, BuildingKind.PowerPlant, 16, 8);
            Mirror(w, BuildingKind.Barracks, 8, 16);
            Mirror(w, BuildingKind.VehicleFactory, 16, 16);
            int first = seed == 0 ? 0 : rng.Next(2);
            int dx = seed == 0 ? 0 : rng.Next(3), dy = seed == 0 ? 0 : rng.Next(3);
            for (int i = 0; i < 4; i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    int team = (first + k) % 2;
                    int x = 12 + dx + i, y = 22 + dy;
                    // the middle of the cell, so the two sides are exact mirror images (a unit exactly on a cell edge is in a different cell when mirrored)
                    var pos = team == 0 ? Centre(x, y) : Centre(Size - 1 - x, Size - 1 - y);
                    w.SpawnUnit(UnitKind.Rifleman, pos, team);
                }
            }
            return w;
        }

        private static Vec2 Centre(int cellX, int cellY) => new Vec2(Fix64.FromFraction(2 * cellX + 1, 2), Fix64.FromFraction(2 * cellY + 1, 2));

        private static void Fill(MapGrid map, int x, int y, int width, int height)
        {
            for (int cy = y; cy < y + height; cy++)
                for (int cx = x; cx < x + width; cx++)
                    map.SetTerrain(cx, cy, TerrainType.Rock);
        }

        private static void Mirror(World w, BuildingKind kind, int x, int y)
        {
            BuildingDef def = Defs.Building(kind);
            w.PlaceBuilding(kind, x, y, 0);
            w.PlaceBuilding(kind, Size - x - def.Width, Size - y - def.Height, 1);
        }
    }
}
