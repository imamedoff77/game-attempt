namespace SimCore
{
    public sealed class Building
    {
        public int Id { get; }
        public BuildingKind Kind { get; }

        /// <summary>Top-left cell of the footprint, and its size in cells.</summary>
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>Energy produced (power plant) or resources per minute produced (supply center), fixed when built.</summary>
        public int Output { get; }

        /// <summary>False while the base lacks the energy to run this building.</summary>
        public bool Powered { get; internal set; } = true;

        internal Building(int id, BuildingKind kind, int x, int y, int width, int height, int output)
        {
            Id = id; Kind = kind; X = x; Y = y; Width = width; Height = height; Output = output;
        }

        public bool Contains(int cellX, int cellY) =>
            cellX >= X && cellX < X + Width && cellY >= Y && cellY < Y + Height;
    }
}
