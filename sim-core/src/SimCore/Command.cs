namespace SimCore
{
    /// <summary>
    /// Commands are the only way the outside world changes the simulation, so they are
    /// also the only thing that has to be sent over the network in multiplayer.
    /// </summary>
    public abstract class Command
    {
    }

    /// <summary>Order to move the given units to a point (they spread into a formation around it).</summary>
    public sealed class MoveCommand : Command
    {
        public int[] UnitIds { get; }
        public Vec2 Target { get; }

        public MoveCommand(int[] unitIds, Vec2 target)
        {
            UnitIds = unitIds;
            Target = target;
        }
    }

    /// <summary>Order to build a building with its top-left corner on cell (X, Y). Ignored if it is not allowed.</summary>
    public sealed class BuildCommand : Command
    {
        public BuildingKind Kind { get; }
        public int X { get; }
        public int Y { get; }

        public BuildCommand(BuildingKind kind, int x, int y)
        {
            Kind = kind; X = x; Y = y;
        }
    }
}
