#nullable disable
using SimCore;

namespace SimCore.Tests;

/// <summary>Plays two computer players against each other without any graphics, for tests and balance runs.</summary>
public static class AiMatch
{
    public readonly record struct Result(int Winner, int Ticks, int Units0, int Units1, ulong Hash);

    /// <summary>Winner is 0 or 1, or -1 when nobody won within the time limit.</summary>
    public static Result Run(AiDifficulty team0, AiDifficulty team1, ulong seed, int maxMinutes = 60, World world = null, Action<World> afterStep = null)
    {
        World w = world ?? SkirmishScenario.Create(seed);
        var ai0 = new AiPlayer(0, team0, seed);
        var ai1 = new AiPlayer(1, team1, seed + 1);
        var buffer = new List<Command>();
        int limit = maxMinutes * 60 * World.TicksPerSecond;
        int winner = -1;

        while (w.Tick < limit)
        {
            foreach (var (ai, team) in new[] { (ai0, 0), (ai1, 1) })
            {
                buffer.Clear();
                ai.Update(w, buffer);
                foreach (Command c in buffer) w.Enqueue(c, team);
            }
            w.Step();
            afterStep?.Invoke(w);

            bool alive0 = w.HasBuilding(BuildingKind.Hq, 0), alive1 = w.HasBuilding(BuildingKind.Hq, 1);
            if (!alive0 || !alive1) { winner = alive0 ? 0 : alive1 ? 1 : -1; break; }
        }
        return new Result(winner, w.Tick, w.Units.Count(u => u.Team == 0), w.Units.Count(u => u.Team == 1), w.StateHash());
    }
}
