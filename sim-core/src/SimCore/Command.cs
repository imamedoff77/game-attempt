namespace SimCore
{
    /// <summary>
    /// Order to move the given units to a point (they spread into a formation around it).
    /// Commands are the only way the outside world changes the simulation, so they are
    /// also the only thing that has to be sent over the network in multiplayer.
    /// </summary>
    public sealed class MoveCommand
    {
        public int[] UnitIds { get; }
        public Vec2 Target { get; }

        public MoveCommand(int[] unitIds, Vec2 target)
        {
            UnitIds = unitIds;
            Target = target;
        }
    }
}
