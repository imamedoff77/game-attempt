#nullable disable
using SimCore;

namespace SimCore.Tests;

/// <summary>Plays a campaign mission as the player with the computer opponent's brain, to check that missions can be won and to measure them.</summary>
public static class MissionBot
{
    public static MissionRun Play(int mission, AiDifficulty player, ulong seed = 1, int maxMinutes = 40)
    {
        var run = new MissionRun(Campaign.Get(mission), seed);
        var ai = new AiPlayer(0, player, seed + 7);
        var buffer = new List<Command>();
        int limit = maxMinutes * 60 * World.TicksPerSecond;
        while (run.Status == MissionStatus.Running && run.World.Tick < limit)
        {
            buffer.Clear();
            ai.Update(run.World, buffer);
            foreach (Command c in buffer) run.World.Enqueue(c, 0);
            run.Advance();
        }
        return run;
    }
}
