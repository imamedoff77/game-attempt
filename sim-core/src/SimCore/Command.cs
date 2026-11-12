namespace SimCore
{
    /// <summary>
    /// Commands are the only way the outside world changes the simulation, so they are
    /// also the only thing that has to be sent over the network in multiplayer.
    /// </summary>
    public abstract class Command
    {
        /// <summary>
        /// The team that gave the order (0 or 1). The server decides it, never the sender; the world only lets a team
        /// command its own units and buildings and spend its own resources.
        /// </summary>
        public int Team { get; set; }
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
        /// <summary>Door side 0..3 (see <see cref="Building.Facing"/>); -1 = the default side for the team.</summary>
        public int Facing { get; }

        public BuildCommand(BuildingKind kind, int x, int y, int facing = -1)
        {
            Kind = kind; X = x; Y = y; Facing = facing;
        }
    }

    /// <summary>
    /// Order to add a unit to a building's production queue. Ignored if not allowed or too expensive.
    /// <see cref="BuildingId"/> 0 means "any": the team's least busy building that can make the unit takes it (load balancing).
    /// </summary>
    public sealed class QueueUnitCommand : Command
    {
        public const int AnyBuilding = 0;
        public int BuildingId { get; }
        public UnitKind Kind { get; }

        public QueueUnitCommand(int buildingId, UnitKind kind)
        {
            BuildingId = buildingId; Kind = kind;
        }
    }

    /// <summary>Order to remove one entry from a production queue; its price is refunded.</summary>
    public sealed class CancelQueuedCommand : Command
    {
        public int BuildingId { get; }
        public int Index { get; }

        public CancelQueuedCommand(int buildingId, int index)
        {
            BuildingId = buildingId; Index = index;
        }
    }

    /// <summary>Order to raise a factory to its next level (costs resources and time). The base must already be at that level.</summary>
    public sealed class UpgradeBuildingCommand : Command
    {
        public int BuildingId { get; }

        public UpgradeBuildingCommand(int buildingId)
        {
            BuildingId = buildingId;
        }
    }

    /// <summary>
    /// Order to set the point new units from a building walk to. For the base (HQ) it sets the point for every production building
    /// of the team, present and future; a single building can be changed again afterwards.
    /// </summary>
    public sealed class SetRallyCommand : Command
    {
        public int BuildingId { get; }
        public Vec2 Point { get; }

        public SetRallyCommand(int buildingId, Vec2 point)
        {
            BuildingId = buildingId; Point = point;
        }
    }

    /// <summary>Order to attack an enemy unit or building. Units walk into range and keep shooting until it is destroyed.</summary>
    public sealed class AttackCommand : Command
    {
        public int[] UnitIds { get; }
        public AttackKind TargetKind { get; }
        public int TargetId { get; }

        public AttackCommand(int[] unitIds, AttackKind targetKind, int targetId)
        {
            UnitIds = unitIds; TargetKind = targetKind; TargetId = targetId;
        }
    }

    /// <summary>Order to stop building something that is still under construction; part of the price is refunded.</summary>
    public sealed class CancelBuildCommand : Command
    {
        public int BuildingId { get; }

        public CancelBuildCommand(int buildingId)
        {
            BuildingId = buildingId;
        }
    }

    /// <summary>Order to start researching the next tier of an upgrade (one research at a time per team).</summary>
    public sealed class ResearchCommand : Command
    {
        public UpgradeKind Kind { get; }

        public ResearchCommand(UpgradeKind kind)
        {
            Kind = kind;
        }
    }

    /// <summary>Order to raise the base to its next level (costs resources and time, then raises the unit limit).</summary>
    public sealed class UpgradeBaseCommand : Command
    {
    }

    /// <summary>The team gives up the match (online: there is no other way to concede). Cannot be undone.</summary>
    public sealed class SurrenderCommand : Command
    {
    }

    /// <summary>Order to halt: drop the current move or attack order and stand still (an idle unit shoots what comes in sight).</summary>
    public sealed class StopCommand : Command
    {
        public int[] UnitIds { get; }

        public StopCommand(int[] unitIds)
        {
            UnitIds = unitIds;
        }
    }

    /// <summary>Order to hold position: stand still and shoot only what comes into weapon range, never chasing.</summary>
    public sealed class HoldCommand : Command
    {
        public int[] UnitIds { get; }

        public HoldCommand(int[] unitIds)
        {
            UnitIds = unitIds;
        }
    }

    /// <summary>Order to move to a point and fight whatever is met on the way, then carry on.</summary>
    public sealed class AttackMoveCommand : Command
    {
        public int[] UnitIds { get; }
        public Vec2 Target { get; }

        public AttackMoveCommand(int[] unitIds, Vec2 target)
        {
            UnitIds = unitIds;
            Target = target;
        }
    }
}
