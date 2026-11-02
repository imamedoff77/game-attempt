namespace SimCore
{
    /// <summary>
    /// Generates the demo desert map from a seed using only integer maths and DeterministicRandom,
    /// so the same seed gives the same map on every device (and later on the server).
    /// </summary>
    public static class DemoMap
    {
        /// <param name="clearX">Centre of an area kept free of obstacles (the player's start).</param>
        public static MapGrid Generate(int width, int height, ulong seed, int clearX, int clearY, int clearRadius)
        {
            var map = new MapGrid(width, height);
            var rng = new DeterministicRandom(seed);

            // Stony patches: walkable, but nothing can be built on them.
            for (int i = 0; i < width * height / 220; i++)
                Blob(map, rng, TerrainType.Stony, rng.Next(width), rng.Next(height), rng.Next(3, 7), rng.Next(3, 7), overwrite: false);

            // Rock ridges: elongated blobs that block movement.
            for (int i = 0; i < width * height / 400; i++)
            {
                bool horizontal = rng.Next(2) == 0;
                int rx = horizontal ? rng.Next(4, 9) : rng.Next(1, 3);
                int ry = horizontal ? rng.Next(1, 3) : rng.Next(4, 9);
                Blob(map, rng, TerrainType.Rock, rng.Next(width), rng.Next(height), rx, ry, overwrite: true);
            }

            // One oasis.
            Blob(map, rng, TerrainType.Water, width * 3 / 4, height / 4, 5, 4, overwrite: true);

            // Keep the start area open sand.
            for (int y = clearY - clearRadius; y <= clearY + clearRadius; y++)
                for (int x = clearX - clearRadius; x <= clearX + clearRadius; x++)
                    if (map.InBounds(x, y)) map.SetTerrain(x, y, TerrainType.Sand);

            return map;
        }

        // Ellipse with a ragged edge. Integer-only.
        private static void Blob(MapGrid map, DeterministicRandom rng, TerrainType type, int cx, int cy, int rx, int ry, bool overwrite)
        {
            int rx2 = rx * rx, ry2 = ry * ry;
            for (int y = cy - ry - 1; y <= cy + ry + 1; y++)
            {
                for (int x = cx - rx - 1; x <= cx + rx + 1; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - cx, dy = y - cy;
                    int inside = dx * dx * ry2 + dy * dy * rx2;       // <= rx2*ry2 means inside the ellipse
                    int limit = rx2 * ry2 + rng.Next(-rx2 * ry2 / 3, rx2 * ry2 / 3 + 1); // ragged edge
                    if (inside > limit) continue;
                    if (!overwrite && map.GetTerrain(x, y) != TerrainType.Sand) continue;
                    map.SetTerrain(x, y, type);
                }
            }
        }
    }
}
