namespace SimCore
{
    /// <summary>One shot, for the view to draw as a tracer. Not part of the game state.</summary>
    public readonly struct ShotEvent
    {
        public Vec2 From { get; }
        public Vec2 To { get; }
        public UnitKind Kind { get; }
        public int Team { get; }

        public ShotEvent(Vec2 from, Vec2 to, UnitKind kind, int team)
        {
            From = from; To = to; Kind = kind; Team = team;
        }
    }
}
