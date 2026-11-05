using System;

namespace SimCore
{
    /// <summary>
    /// The lifetime numbers shown on the profile screen. Only ever grow, so two copies (two devices) are joined by taking the larger
    /// of each number. Counted locally for now; the server will count online matches itself (Faza 5).
    /// </summary>
    public sealed class PlayerStats
    {
        private const int Fields = 5;
        private readonly int[] _v = new int[Fields];

        /// <summary>Matches finished against the computer (giving up in the first minute is not counted).</summary>
        public int MatchesPlayed => _v[0];
        public int MatchesWon => _v[1];
        public int MissionsWon => _v[2];
        public int UnitsBuilt => _v[3];
        public int EnemiesDestroyed => _v[4];

        public int WinPercent => MatchesPlayed == 0 ? 0 : Math.Min(100, MatchesWon * 100 / MatchesPlayed);

        /// <summary>Adds one finished match. <paramref name="missionWon"/> is a won campaign mission.</summary>
        public void RecordMatch(bool counted, bool won, bool missionWon, int unitsBuilt, int enemiesDestroyed)
        {
            if (counted) _v[0]++;
            if (won) _v[1]++;
            if (missionWon) _v[2]++;
            _v[3] += Math.Max(0, unitsBuilt);
            _v[4] += Math.Max(0, enemiesDestroyed);
        }

        public void MergeFrom(PlayerStats other)
        {
            for (int i = 0; i < Fields; i++) _v[i] = Math.Max(_v[i], other._v[i]);
        }

        public string Serialize() => string.Join(",", _v);

        public static PlayerStats Parse(string text)
        {
            var stats = new PlayerStats();
            if (string.IsNullOrEmpty(text)) return stats;
            string[] p = text.Split(',');
            if (p.Length != Fields) return stats;
            var v = new int[Fields];
            for (int i = 0; i < Fields; i++) if (!int.TryParse(p[i], out v[i])) return stats;
            for (int i = 0; i < Fields; i++) stats._v[i] = Math.Max(0, v[i]);
            return stats;
        }
    }
}
