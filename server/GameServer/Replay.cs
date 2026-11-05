using SimCore;

namespace GameServer;

/// <summary>
/// Plays a finished (or half finished) match again from the turns the relay recorded and says who won. The players' own word is never
/// trusted for ratings: the replay of the same deterministic simulation is the truth.
/// </summary>
internal static class Replay
{
    // A long match takes some seconds of one processor; never let many of them pile up.
    private static readonly SemaphoreSlim Gate = new(2);

    /// <summary>A team (0 or 1), <see cref="MatchReferee.Ongoing"/> or <see cref="MatchReferee.Draw"/>. Runs the whole match: call it off the room lock.</summary>
    public static int Verdict(IReadOnlyList<TurnBundle> log, byte[][] kits)
    {
        Gate.Wait();
        try
        {
            World world = OnlineScenario.Create();
            byte[][] clean = NetKits.Normalize(kits);
            for (int team = 0; team < World.TeamCount; team++) Armory.FromBytes(clean[team]).Apply(world, team);
            foreach (TurnBundle bundle in log)
            {
                for (int team = 0; team < World.TeamCount; team++)
                    foreach (Command c in bundle.ByTeam[team])
                        world.Enqueue(c, team);
                for (int tick = 0; tick < LockstepClient.TicksPerTurn; tick++)
                {
                    world.Step();
                    int winner = MatchReferee.Winner(world);
                    if (winner != MatchReferee.Ongoing) return winner;
                }
            }
            return MatchReferee.Ongoing;
        }
        finally { Gate.Release(); }
    }
}
