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

        internal Building(int id, BuildingKind kind, int x, int y, int width, int height)
        {
            Id = id; Kind = kind; X = x; Y = y; Width = width; Height = height;
        }
    }
}
