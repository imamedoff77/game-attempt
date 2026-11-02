namespace SimCore
{
    public enum TerrainType : byte
    {
        /// <summary>Walkable, buildable.</summary>
        Sand = 0,
        /// <summary>Walkable, NOT buildable.</summary>
        Stony = 1,
        /// <summary>Blocks movement, not buildable.</summary>
        Rock = 2,
        /// <summary>Blocks movement, not buildable (shore buildings such as shipyards come later).</summary>
        Water = 3,
    }

    /// <summary>The map: a grid of 1x1 cells with a terrain type each, plus which building (if any) covers it.</summary>
    public sealed class MapGrid
    {
        private readonly TerrainType[] _terrain;
        private readonly int[] _building; // 0 = no building

        public int Width { get; }
        public int Height { get; }

        /// <summary>All-sand map.</summary>
        public MapGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _terrain = new TerrainType[width * height];
            _building = new int[width * height];
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public TerrainType GetTerrain(int x, int y) => _terrain[y * Width + x];
        public void SetTerrain(int x, int y, TerrainType t) => _terrain[y * Width + x] = t;

        public int BuildingAt(int x, int y) => _building[y * Width + x];
        internal void SetBuilding(int x, int y, int buildingId) => _building[y * Width + x] = buildingId;

        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            TerrainType t = _terrain[y * Width + x];
            return (t == TerrainType.Sand || t == TerrainType.Stony) && _building[y * Width + x] == 0;
        }

        /// <summary>True when the whole footprint is inside the map, on sand, and free of other buildings.</summary>
        public bool CanPlace(int x, int y, int w, int h)
        {
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    if (!InBounds(cx, cy)) return false;
                    if (_terrain[cy * Width + cx] != TerrainType.Sand) return false;
                    if (_building[cy * Width + cx] != 0) return false;
                }
            }
            return true;
        }

        public ulong Hash()
        {
            StateHasher h = StateHasher.Create();
            h.Add(Width);
            h.Add(Height);
            for (int i = 0; i < _terrain.Length; i++)
            {
                h.Add((int)_terrain[i]);
                h.Add(_building[i]);
            }
            return h.Value;
        }
    }
}
