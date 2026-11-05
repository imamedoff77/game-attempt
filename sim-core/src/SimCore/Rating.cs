using System;

namespace SimCore
{
    /// <summary>
    /// The Elo rating of ranked matches: everybody starts at <see cref="Start"/>, a win against a stronger player pays more than a win against a weaker one.
    /// New players move faster (a bigger K) until they have played <see cref="SettledGames"/> ranked matches.
    /// Only the servers use this, so plain floating point is fine.
    /// </summary>
    public static class Rating
    {
        public const int Start = 1000;
        public const int Floor = 100;
        public const int SettledGames = 30;

        public static int KFactor(int gamesPlayed) => gamesPlayed < SettledGames ? 32 : 20;

        /// <summary>The chance (0..1) that a player rated <paramref name="rating"/> beats one rated <paramref name="opponent"/>.</summary>
        public static double ExpectedScore(int rating, int opponent) => 1.0 / (1.0 + Math.Pow(10.0, (opponent - rating) / 400.0));

        /// <summary>The new ratings after <paramref name="winner"/> beat <paramref name="loser"/> (each side moves by its own K, the loser never drops below <see cref="Floor"/>).</summary>
        public static void Apply(int winner, int winnerGames, int loser, int loserGames, out int winnerNew, out int loserNew)
        {
            double expectedWinner = ExpectedScore(winner, loser);
            int gain = Math.Max(1, (int)Math.Round(KFactor(winnerGames) * (1.0 - expectedWinner)));
            int loss = Math.Max(1, (int)Math.Round(KFactor(loserGames) * (1.0 - expectedWinner)));
            winnerNew = winner + gain;
            loserNew = Math.Max(Floor, loser - loss);
        }
    }
}
