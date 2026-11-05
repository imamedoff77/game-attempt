namespace SimCore
{
    /// <summary>The rules for who has won a 1v1 match, in one place: used by the server to check what the players claim by replaying the match.</summary>
    public static class MatchReferee
    {
        public const int Ongoing = -1;
        /// <summary>Both sides lost at the very same moment: nobody won.</summary>
        public const int Draw = -2;

        /// <summary>The winning team (0 or 1), <see cref="Ongoing"/> or <see cref="Draw"/>. Surrendering comes first, then a destroyed headquarters.</summary>
        public static int Winner(World world)
        {
            bool s0 = world.HasSurrendered(0), s1 = world.HasSurrendered(1);
            if (s0 != s1) return s0 ? 1 : 0;
            if (s0) return Draw;
            bool hq0 = world.HasBuilding(BuildingKind.Hq, 0), hq1 = world.HasBuilding(BuildingKind.Hq, 1);
            if (hq0 && hq1) return Ongoing;
            if (hq0) return 0;
            if (hq1) return 1;
            return Draw;
        }
    }
}
